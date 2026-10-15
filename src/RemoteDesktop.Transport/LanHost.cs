using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RemoteDesktop.Core;

namespace RemoteDesktop.Transport;

public sealed class LanHost : IDisposable, IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly X509Certificate2 certificate;
    private readonly byte[] pairingToken = RandomNumberGenerator.GetBytes(32);
    private readonly LanOptions options;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object lifecycle = new();
    private TcpClient? active;
    private Task? operation, disposal;
    private bool disposed;
    private int state = (int)LanSessionState.Listening;
    public IPEndPoint Endpoint { get; }
    public string Fingerprint { get; }
    public string PairingCode { get; }
    public DateTimeOffset ExpiresAt { get; }
    public LanSessionState State => (LanSessionState)Volatile.Read(ref state);
    public event Action<LanSessionState>? StateChanged;

    public LanHost(IPAddress bindAddress, int port = 0, LanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bindAddress);
        if (!LanAddressPolicy.IsAllowed(bindAddress)) throw new ArgumentException("Host must bind to a specific private IPv4 or loopback address.", nameof(bindAddress));
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        this.options = options ?? new(); this.options.Validate();
        certificate = LanCertificate.Create();
        Fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        PairingCode = Convert.ToBase64String(pairingToken);
        ExpiresAt = DateTimeOffset.UtcNow + this.options.PairingLifetime;
        listener = new(bindAddress, port);
        try { listener.Start(1); Endpoint = (IPEndPoint)listener.LocalEndpoint; }
        catch { listener.Stop(); certificate.Dispose(); lifetime.Dispose(); CryptographicOperations.ZeroMemory(pairingToken); throw; }
    }

    public Task RunAsync(Func<ViewerHello, CancellationToken, Task<bool>> approve, Func<CancellationToken, IAsyncEnumerable<EncodedFrame>> frames, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(approve); ArgumentNullException.ThrowIfNull(frames);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (operation != null) throw new InvalidOperationException("A host serves one approved session. Create a new host to restart.");
            return operation = RunCoreAsync(approve, frames, token);
        }
    }

    private async Task RunCoreAsync(Func<ViewerHello, CancellationToken, Task<bool>> approve, Func<CancellationToken, IAsyncEnumerable<EncodedFrame>> frames, CancellationToken token)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        using var pairing = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
        var remaining = ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) pairing.Cancel(); else pairing.CancelAfter(remaining);
        var failures = 0;
        try
        {
            while (!pairing.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(pairing.Token).ConfigureAwait(false);
                lock (lifecycle) { if (disposed) return; active = client; }
                client.NoDelay = true;
                await using var secure = new SslStream(client.GetStream(), false);
                try
                {
                    SetState(LanSessionState.Authenticating);
                    using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(pairing.Token))
                    {
                        handshake.CancelAfter(options.HandshakeTimeout);
                        await secure.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                        {
                            ServerCertificate = certificate, ClientCertificateRequired = false,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                        }, handshake.Token).ConfigureAwait(false);
                    }
                    var connection = new TransportConnection(secure, options);
                    var auth = await connection.ReadAsync(pairing.Token, options.HandshakeTimeout).ConfigureAwait(false);
                    if (auth.Type != MessageType.Authentication) throw new InvalidDataException("Authentication must be the first message.");
                    var (providedToken, hello) = ProtocolCodec.DecodeAuthentication(auth.Payload);
                    bool valid;
                    try { valid = DateTimeOffset.UtcNow < ExpiresAt && CryptographicOperations.FixedTimeEquals(providedToken, pairingToken); }
                    finally { CryptographicOperations.ZeroMemory(providedToken); CryptographicOperations.ZeroMemory(auth.Payload); }
                    if (!valid)
                    {
                        await connection.WriteAsync(MessageType.Rejected, [], pairing.Token).ConfigureAwait(false);
                        throw new AuthenticationException("Invalid or expired pairing code.");
                    }
                    SetState(LanSessionState.AwaitingApproval);
                    using var approval = CancellationTokenSource.CreateLinkedTokenSource(pairing.Token);
                    approval.CancelAfter(options.ApprovalTimeout);
                    var approvalTask = approve(hello, approval.Token);
                    bool accepted;
                    try { accepted = await approvalTask.WaitAsync(approval.Token).ConfigureAwait(false); }
                    finally { Observe(approvalTask); }
                    if (!accepted)
                    {
                        await connection.WriteAsync(MessageType.Rejected, [], pairing.Token).ConfigureAwait(false);
                        SetState(LanSessionState.Closed); return;
                    }
                    await connection.WriteAsync(MessageType.Approved, [], pairing.Token).ConfigureAwait(false);
                    SetState(LanSessionState.Streaming);
                    using var session = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
                    session.CancelAfter(options.SessionLifetime);
                    await StreamAsync(connection, frames, session).ConfigureAwait(false);
                    SetState(LanSessionState.Closed); return;
                }
                catch (Exception error) when (State != LanSessionState.Streaming && IsAttemptFailure(error) && !pairing.IsCancellationRequested)
                {
                    if (++failures >= options.MaxFailedAttempts) throw new AuthenticationException("Pairing attempt limit reached.", error);
                    SetState(LanSessionState.Listening);
                }
                finally { lock (lifecycle) { if (ReferenceEquals(active, client)) active = null; } }
            }
            SetState(LanSessionState.Closed);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested || pairing.IsCancellationRequested) { SetState(LanSessionState.Closed); }
        catch (Exception error) when (run.IsCancellationRequested && error is IOException or SocketException or ObjectDisposedException) { SetState(LanSessionState.Closed); }
        catch { SetState(LanSessionState.Failed); throw; }
        finally { listener.Stop(); }
    }

    private async Task StreamAsync(TransportConnection connection, Func<CancellationToken, IAsyncEnumerable<EncodedFrame>> frames, CancellationTokenSource session)
    {
        var sender = SendFramesAsync(connection, frames, session.Token);
        var receiver = ReceiveControlAsync(connection, session.Token);
        var heartbeats = connection.HeartbeatsAsync(session.Token);
        try
        {
            var first = await Task.WhenAny(sender, receiver, heartbeats).ConfigureAwait(false);
            await first.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (EndOfStreamException) { }
        catch (IOException) { } // A peer leaving the LAN ends this session.
        finally
        {
            session.Cancel();
            await ObserveCompletionAsync(sender, receiver, heartbeats).ConfigureAwait(false);
        }
    }

    private static async Task SendFramesAsync(TransportConnection connection, Func<CancellationToken, IAsyncEnumerable<EncodedFrame>> frames, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var last = TimeSpan.FromSeconds(-1);
        await foreach (var frame in frames(token).WithCancellation(token).ConfigureAwait(false))
        {
            var delay = TimeSpan.FromMilliseconds(100) - (clock.Elapsed - last);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            await connection.WriteAsync(MessageType.Frame, ProtocolCodec.EncodeFrame(frame), token).ConfigureAwait(false);
            last = clock.Elapsed;
        }
        await connection.WriteAsync(MessageType.Disconnect, [], token).ConfigureAwait(false);
    }

    private static async Task ReceiveControlAsync(TransportConnection connection, CancellationToken token)
    {
        while (true)
        {
            var message = await connection.ReadAsync(token).ConfigureAwait(false);
            if (message.Type == MessageType.Disconnect) return;
            if (message.Type != MessageType.Heartbeat) throw new InvalidDataException("Viewer sent an unsupported message.");
        }
    }

    private static bool IsAttemptFailure(Exception error) => error is AuthenticationException or InvalidDataException or IOException or SocketException or TimeoutException or OperationCanceledException;
    private void SetState(LanSessionState next)
    {
        Volatile.Write(ref state, (int)next);
        StateChanged?.Invoke(next);
    }
    internal static void Observe(Task task) => _ = task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    internal static async Task ObserveCompletionAsync(params Task[] tasks)
    {
        // Every worker is cancelled before this join; no detached socket operations remain.
        foreach (var task in tasks) { try { await task.ConfigureAwait(false); } catch { } }
    }

    public void Dispose() => Observe(BeginDispose());
    public ValueTask DisposeAsync() => new(BeginDispose());
    private Task BeginDispose()
    {
        lock (lifecycle)
        {
            if (disposal != null) return disposal;
            disposed = true;
            lifetime.Cancel(); listener.Stop(); active?.Dispose();
            return disposal = DisposeCoreAsync(operation);
        }
    }
    private async Task DisposeCoreAsync(Task? running)
    {
        try { if (running != null) await running.ConfigureAwait(false); }
        catch { /* RunAsync remains the caller's error-reporting task. */ }
        finally
        {
            certificate.Dispose(); lifetime.Dispose(); CryptographicOperations.ZeroMemory(pairingToken);
            if (State != LanSessionState.Failed) SetState(LanSessionState.Closed);
        }
    }
}
