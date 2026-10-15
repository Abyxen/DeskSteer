using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using RemoteDesktop.Core;
using RemoteDesktop.Transport;
using Xunit;

namespace RemoteDesktop.Transport.Tests;

public sealed class LoopbackTlsTests
{
    private static readonly EncodedFrame Sample = FrameFixture.Create();

    [Fact]
    public async Task ApprovedViewerReceivesUnchangedFrameThroughActualTls()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var host = new LanHost(IPAddress.Loopback);
        var approval = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = host.RunAsync((hello, _) => { approval.TrySetResult(hello.DisplayName); return Task.FromResult(true); }, OneFrame, lifetime.Token);
        await using var viewer = await LanViewer.ConnectAsync(Request(host), lifetime.Token);
        await using var frames = viewer.FramesAsync(lifetime.Token).GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync());
        Assert.Equal("Transport test viewer", await approval.Task.WaitAsync(lifetime.Token));
        Assert.Equal(Sample.Width, frames.Current.Width);
        Assert.Equal(Sample.Height, frames.Current.Height);
        Assert.Equal(Sample.TimestampTicks, frames.Current.TimestampTicks);
        Assert.Equal(Sample.Jpeg, frames.Current.Jpeg);
        await viewer.DisconnectAsync();
        await run.WaitAsync(lifetime.Token);
    }

    [Fact]
    public async Task IncorrectCertificatePinPreventsApprovalAndFrameProduction()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var host = new LanHost(IPAddress.Loopback);
        var approvals = 0;
        var productions = 0;
        var run = host.RunAsync((_, _) => { Interlocked.Increment(ref approvals); return Task.FromResult(true); }, token => { Interlocked.Increment(ref productions); return OneFrame(token); }, lifetime.Token);
        var request = Request(host) with { Fingerprint = new string('0', 64) };
        var error = await Record.ExceptionAsync(async () => { await using var _ = await LanViewer.ConnectAsync(request, lifetime.Token); });
        Assert.NotNull(error);
        Assert.False(error is OperationCanceledException, error.ToString());
        Assert.Equal(0, approvals);
        Assert.Equal(0, productions);
        lifetime.Cancel();
        await ObserveRun(run);
    }

    [Fact]
    public async Task WrongTemporarySecretIsRejectedBeforeApprovalOrCapture()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var host = new LanHost(IPAddress.Loopback);
        var approvals = 0;
        var productions = 0;
        var run = host.RunAsync((_, _) => { Interlocked.Increment(ref approvals); return Task.FromResult(true); }, token => { Interlocked.Increment(ref productions); return OneFrame(token); }, lifetime.Token);
        var request = Request(host) with { PairingCode = Convert.ToBase64String(new byte[32]) };
        var error = await Record.ExceptionAsync(async () => { await using var _ = await LanViewer.ConnectAsync(request, lifetime.Token); });
        Assert.NotNull(error);
        Assert.False(error is OperationCanceledException, error.ToString());
        Assert.Equal(0, approvals);
        Assert.Equal(0, productions);
        lifetime.Cancel();
        await ObserveRun(run);
    }

    [Fact]
    public async Task DeclinedHostApprovalNeverStartsFrameSource()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var host = new LanHost(IPAddress.Loopback);
        var approvals = 0;
        var productions = 0;
        var run = host.RunAsync((_, _) => { Interlocked.Increment(ref approvals); return Task.FromResult(false); }, token => { Interlocked.Increment(ref productions); return OneFrame(token); }, lifetime.Token);
        var error = await Record.ExceptionAsync(async () => { await using var _ = await LanViewer.ConnectAsync(Request(host), lifetime.Token); });
        Assert.NotNull(error);
        Assert.False(error is OperationCanceledException, error.ToString());
        Assert.Equal(1, approvals);
        Assert.Equal(0, productions);
        lifetime.Cancel();
        await ObserveRun(run);
    }

    [Fact]
    public async Task CancelledConnectionDoesNotReachHostApproval()
    {
        await using var host = new LanHost(IPAddress.Loopback);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await using var _ = await LanViewer.ConnectAsync(Request(host), cancelled.Token); });
    }

    [Fact]
    public async Task NewHostHasDifferentCertificateAndTemporarySecret()
    {
        string oldFingerprint, oldCode;
        int oldPort;
        await using (var first = new LanHost(IPAddress.Loopback))
        {
            oldFingerprint = first.Fingerprint; oldCode = first.PairingCode; oldPort = first.Endpoint.Port;
        }
        await using var second = new LanHost(IPAddress.Loopback, oldPort);
        Assert.Equal(oldPort, second.Endpoint.Port);
        Assert.NotEqual(oldFingerprint, second.Fingerprint);
        Assert.NotEqual(oldCode, second.PairingCode);
    }

    [Fact]
    public async Task ApprovalDeadlineRejectsViewerWithoutStartingCapture()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var options = new LanOptions { ApprovalTimeout = TimeSpan.FromMilliseconds(150) };
        await using var host = new LanHost(IPAddress.Loopback, options: options);
        var enteredApproval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var productions = 0;
        var run = host.RunAsync(async (_, token) =>
        {
            enteredApproval.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return true;
        }, token => { Interlocked.Increment(ref productions); return OneFrame(token); }, lifetime.Token);
        var connecting = LanViewer.ConnectAsync(Request(host), lifetime.Token, options);
        await enteredApproval.Task.WaitAsync(lifetime.Token);
        var error = await Record.ExceptionAsync(async () => { await using var _ = await connecting; });
        Assert.NotNull(error);
        Assert.False(lifetime.IsCancellationRequested, "The approval deadline must expire before the overall test deadline.");
        Assert.Equal(0, productions);
        lifetime.Cancel();
        await ObserveRun(run);
    }

    [Fact]
    public async Task ViewerCancellationWhileAwaitingApprovalClosesTheConnection()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var viewerCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        await using var host = new LanHost(IPAddress.Loopback);
        var enteredApproval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = host.RunAsync(async (_, token) =>
        {
            enteredApproval.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return true;
        }, OneFrame, lifetime.Token);
        var connecting = LanViewer.ConnectAsync(Request(host), viewerCancellation.Token);
        await enteredApproval.Task.WaitAsync(lifetime.Token);
        viewerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await using var _ = await connecting; });
        lifetime.Cancel();
        await ObserveRun(run);
    }

    [Fact]
    public async Task OldPairingCodeCannotAuthenticateToAReplacementHost()
    {
        string staleCode;
        await using (var previous = new LanHost(IPAddress.Loopback)) staleCode = previous.PairingCode;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var host = new LanHost(IPAddress.Loopback);
        var approvals = 0;
        var run = host.RunAsync((_, _) => { Interlocked.Increment(ref approvals); return Task.FromResult(true); }, OneFrame, lifetime.Token);
        var error = await Record.ExceptionAsync(async () => { await using var _ = await LanViewer.ConnectAsync(Request(host) with { PairingCode = staleCode }, lifetime.Token); });
        Assert.IsType<System.Security.Authentication.AuthenticationException>(error);
        Assert.Equal(0, approvals);
        lifetime.Cancel();
        await ObserveRun(run);
    }

    [Fact]
    public async Task IdleFrameSourceRemainsConnectedThroughBidirectionalHeartbeats()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var options = new LanOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(80), ReadWriteTimeout = TimeSpan.FromMilliseconds(400) };
        await using var host = new LanHost(IPAddress.Loopback, options: options);
        var run = host.RunAsync((_, _) => Task.FromResult(true), DelayedFrames, lifetime.Token);
        await using var viewer = await LanViewer.ConnectAsync(Request(host), lifetime.Token, options);
        await using var frames = viewer.FramesAsync(lifetime.Token).GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync());
        Assert.Equal(Sample.TimestampTicks, frames.Current.TimestampTicks);
        Assert.True(await frames.MoveNextAsync());
        Assert.Equal(Sample.TimestampTicks + 1, frames.Current.TimestampTicks);
        await viewer.DisconnectAsync();
        await run.WaitAsync(lifetime.Token);
    }

    [Fact]
    public async Task StalledRealTcpHandshakeTimesOutAndClosesHostAtAttemptLimit()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var options = new LanOptions { HandshakeTimeout = TimeSpan.FromMilliseconds(200), MaxFailedAttempts = 1 };
        await using var host = new LanHost(IPAddress.Loopback, options: options);
        var approvals = 0;
        var run = host.RunAsync((_, _) => { Interlocked.Increment(ref approvals); return Task.FromResult(true); }, OneFrame, lifetime.Token);
        using var stalled = new TcpClient();
        await stalled.ConnectAsync(host.Endpoint.Address, host.Endpoint.Port, lifetime.Token);
        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => run.WaitAsync(lifetime.Token));
        Assert.Equal(0, approvals);
        Assert.Equal(LanSessionState.Failed, host.State);
    }

    [Fact]
    public async Task PairingDeadlineClosesUnusedListener()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var host = new LanHost(IPAddress.Loopback, options: new LanOptions { PairingLifetime = TimeSpan.FromMilliseconds(150) });
        var run = host.RunAsync((_, _) => Task.FromResult(true), OneFrame, lifetime.Token);
        await run.WaitAsync(lifetime.Token);
        Assert.Equal(LanSessionState.Closed, host.State);
        using var client = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync(host.Endpoint.Address, host.Endpoint.Port, lifetime.Token).AsTask());
    }

    internal static ConnectionRequest Request(LanHost host) => new("127.0.0.1", host.Endpoint.Port, host.Fingerprint, host.PairingCode, "Transport test viewer");
    private static async IAsyncEnumerable<EncodedFrame> OneFrame([EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        yield return Sample;
        await Task.Delay(Timeout.Infinite, token);
    }
    private static async IAsyncEnumerable<EncodedFrame> DelayedFrames([EnumeratorCancellation] CancellationToken token)
    {
        yield return Sample;
        await Task.Delay(900, token);
        yield return Sample with { TimestampTicks = Sample.TimestampTicks + 1 };
        await Task.Delay(Timeout.Infinite, token);
    }
    private static async Task ObserveRun(Task run)
    {
        try { await run.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (OperationCanceledException) { }
        catch (System.Security.Authentication.AuthenticationException) { }
        catch (IOException) { }
    }
}

