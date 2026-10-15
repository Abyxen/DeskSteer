using System.Diagnostics;
using DeskSteer.Core;

namespace DeskSteer.Transport;

// All authority checks and native sink calls share this gate. A local revoke
// cannot return while an earlier Apply is still executing, and future Apply
// calls can never cross the revoked authority boundary.
internal sealed class HostControlSession
{
    private readonly object gate = new();
    private readonly object transitions = new();
    private readonly TransportConnection connection;
    private readonly ViewerHello viewer;
    private readonly Func<ViewerHello, CancellationToken, Task<bool>>? approve;
    private readonly IRemoteInputSink? input;
    private readonly LanOptions options;
    private readonly CancellationTokenSource session;
    private readonly CancellationTokenRegistration stoppedRegistration;
    private readonly TaskCompletionSource failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? pending;
    private Task approval = Task.CompletedTask, notifications = Task.CompletedTask;
    private long generation, lastRequest, window;
    private int inputCount;
    private bool requested, authority, stopped;
    internal bool HasControl { get { lock (gate) return authority; } }
    internal Task Failure => failure.Task;
    internal event Action<bool>? ControlChanged;

    internal HostControlSession(TransportConnection connection, ViewerHello viewer, Func<ViewerHello, CancellationToken, Task<bool>>? approve, IRemoteInputSink? input, LanOptions options, CancellationTokenSource session)
    {
        this.connection = connection; this.viewer = viewer; this.approve = approve; this.input = input; this.options = options; this.session = session;
        stoppedRegistration = session.Token.Register(() => RevokeLocal(stop: true, notify: false));
    }

    internal void Request()
    {
        lock (gate)
        {
            if (stopped) throw new OperationCanceledException(session.Token);
            var now = Stopwatch.GetTimestamp();
            if (authority || pending != null || requested && Stopwatch.GetElapsedTime(lastRequest, now) < TimeSpan.FromSeconds(1))
                throw new InvalidDataException("Control requests are limited to one pending request and one per second.");
            requested = true; lastRequest = now;
            if (approve == null || input == null) { QueueNotificationLocked(MessageType.ControlRevoked); return; }
            var source = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
            source.CancelAfter(options.ApprovalTimeout);
            pending = source;
            var requestGeneration = ++generation;
            approval = ApproveAsync(source, requestGeneration);
        }
    }

    private async Task ApproveAsync(CancellationTokenSource source, long requestGeneration)
    {
        // Publish the worker before a synchronously-completing approval can clear it.
        await Task.Yield();
        Task<bool>? callback = null;
        var changed = false;
        Exception? notificationError = null;
        try
        {
            source.Token.ThrowIfCancellationRequested();
            callback = approve!(viewer, source.Token);
            var accepted = await callback.WaitAsync(source.Token).ConfigureAwait(false);
            source.Token.ThrowIfCancellationRequested();
            lock (transitions)
            {
                lock (gate)
                {
                    if (stopped || requestGeneration != generation || !ReferenceEquals(pending, source)) return;
                    pending = null;
                    if (accepted)
                    {
                        authority = true; changed = true;
                        window = Stopwatch.GetTimestamp() / Stopwatch.Frequency; inputCount = 0;
                    }
                    QueueNotificationLocked(accepted ? MessageType.ControlGranted : MessageType.ControlRevoked);
                }
                if (changed) notificationError = RaiseChanged(true);
            }
            if (notificationError != null) Fail(notificationError);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            lock (gate)
            {
                if (stopped || requestGeneration != generation || !ReferenceEquals(pending, source)) return;
                pending = null;
                QueueNotificationLocked(MessageType.ControlRevoked);
            }
        }
        catch (Exception error) { Fail(error); }
        finally
        {
            if (callback != null) LanHost.Observe(callback);
            source.Dispose();
        }
    }

    internal void Apply(RemoteInputCommand command)
    {
        RemoteInputPolicy.Validate(command);
        lock (gate)
        {
            if (stopped || !authority || input == null) throw new InvalidDataException("Remote input requires current host approval.");
            var currentWindow = Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            if (currentWindow != window) { window = currentWindow; inputCount = 0; }
            if (++inputCount > 200) throw new InvalidDataException("Remote input exceeds the 200-per-second limit.");
            input.Apply(command);
        }
    }

    internal Task RevokeAsync() => RevokeLocal(stop: false, notify: true);

    private Task RevokeLocal(bool stop, bool notify)
    {
        CancellationTokenSource? cancel;
        bool changed;
        Task notification;
        Exception? error = null;
        lock (transitions)
        {
            lock (gate)
            {
                if (stopped) return notifications;
                if (stop) stopped = true;
                ++generation;
                changed = authority; authority = false;
                cancel = pending; pending = null;
                // Native releases happen before notification queuing or any await.
                try { input?.ReleaseAll(); }
                catch (Exception releaseError) { error = releaseError; }
                if (notify) QueueNotificationLocked(MessageType.ControlRevoked);
                notification = notifications;
            }
            var cancellationError = CancelSafely(cancel);
            error ??= cancellationError;
            if (changed)
            {
                var changeError = RaiseChanged(false);
                error ??= changeError;
            }
        }
        // A failed native release must end the session, but cannot interrupt
        // authority invalidation, pending-prompt cancellation or the local indicator.
        // In particular, cancellation callbacks must never throw into CTS.Cancel.
        if (error != null) Fail(error);
        return notification;
    }

    private void QueueNotificationLocked(MessageType type)
    {
        // Preserve grant/revoke order even when TLS is busy sending a frame. A
        // revoke that wins the authority gate invalidates any late approval.
        notifications = SendNotificationAsync(notifications, type);
        LanHost.Observe(notifications);
    }

    private async Task SendNotificationAsync(Task previous, MessageType type)
    {
        // Never fail/cancel the session inline while the authority gate is held.
        await Task.Yield();
        try
        {
            await previous.ConfigureAwait(false);
            await connection.WriteAsync(type, [], session.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
    }

    private Exception? RaiseChanged(bool value)
    {
        try { ControlChanged?.Invoke(value); return null; }
        catch (Exception error) { return error; }
    }
    private void Fail(Exception error)
    {
        failure.TrySetException(error);
        // Other callback owners must not be able to interrupt transport cleanup.
        try { session.Cancel(); } catch (Exception cancellationError) { failure.TrySetException(cancellationError); }
    }
    private static Exception? CancelSafely(CancellationTokenSource? source)
    {
        try { source?.Cancel(); return null; }
        catch (ObjectDisposedException) { return null; }
        catch (Exception error) { return error; }
    }

    internal async Task StopAsync()
    {
        try
        {
            _ = RevokeLocal(stop: true, notify: false);
            Task pendingApproval, pendingNotifications;
            lock (gate) { pendingApproval = approval; pendingNotifications = notifications; }
            await LanHost.ObserveCompletionAsync(pendingApproval, pendingNotifications).ConfigureAwait(false);
        }
        finally { stoppedRegistration.Dispose(); LanHost.Observe(failure.Task); }
    }
}
