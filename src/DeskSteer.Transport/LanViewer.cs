using System.Net.Security;
using System.Net.Sockets;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DeskSteer.Core;

namespace DeskSteer.Transport;

public sealed class LanViewer : IAsyncDisposable
{
    private readonly TcpClient client;
    private readonly SslStream secure;
    private readonly TransportConnection connection;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object lifecycle = new();
    private readonly Task heartbeats;
    private readonly CancellationTokenRegistration lifetimeClosed;
    private Task? disconnect;
    private bool disposed;
    private int consuming;
    private int hasControl;
    private bool requestPending, requestedControl;
    private long lastControlRequest;
    public bool HasControl => Volatile.Read(ref hasControl) != 0;
    public event Action<bool>? ControlChanged;

    private LanViewer(TcpClient client, SslStream secure, TransportConnection connection, LanOptions options)
    {
        this.client = client; this.secure = secure; this.connection = connection;
        // Session expiry must release the socket even when no consumer is reading,
        // or an iterator is suspended after yielding its last displayed frame.
        lifetimeClosed = lifetime.Token.Register(static state => ((LanViewer)state!).OnLifetimeEnded(), this);
        lifetime.CancelAfter(options.SessionLifetime);
        heartbeats = SendHeartbeatsAsync();
    }

    public static async Task<LanViewer> ConnectAsync(ConnectionRequest request, CancellationToken token = default, LanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        options ??= new(); options.Validate();
        var address = LanAddressPolicy.Parse(request.Address);
        if (request.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(request.Port));
        byte[] pin, pairing;
        try
        {
            pin = Convert.FromHexString(request.Fingerprint);
            pairing = Convert.FromBase64String(request.PairingCode);
        }
        catch (FormatException error) { throw new ArgumentException("Fingerprint or pairing code has an invalid format.", nameof(request), error); }
        if (pin.Length != 32 || pairing.Length != 32) throw new ArgumentException("Fingerprint and pairing code must each identify 32 bytes.", nameof(request));
        var authentication = ProtocolCodec.EncodeAuthentication(pairing, request.ViewerName);
        CryptographicOperations.ZeroMemory(pairing);
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        SslStream? secure = null;
        try
        {
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                handshake.CancelAfter(options.HandshakeTimeout);
                try
                {
                    await client.ConnectAsync(address, request.Port, handshake.Token).ConfigureAwait(false);
                    secure = new SslStream(client.GetStream(), false, (_, certificate, _, errors) => LanCertificate.Validate(certificate, errors, pin));
                    await secure.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = LanCertificate.HostName, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    }, handshake.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException error) when (!token.IsCancellationRequested)
                { throw new TimeoutException("LAN connection or TLS handshake timed out.", error); }
            }
            var connection = new TransportConnection(secure, options);
            await connection.WriteAsync(MessageType.Authentication, authentication, token).ConfigureAwait(false);
            var answer = await connection.ReadAsync(token, options.ApprovalTimeout + options.HandshakeTimeout).ConfigureAwait(false);
            if (answer.Type == MessageType.Rejected) throw new AuthenticationException("The host rejected this pairing request.");
            if (answer.Type != MessageType.Approved) throw new InvalidDataException("Host did not approve this pairing request.");
            return new LanViewer(client, secure, connection, options);
        }
        catch
        {
            if (secure != null) await secure.DisposeAsync().ConfigureAwait(false);
            client.Dispose(); throw;
        }
        finally { CryptographicOperations.ZeroMemory(authentication); }
    }

    public async IAsyncEnumerable<EncodedFrame> FramesAsync([EnumeratorCancellation] CancellationToken token = default)
    {
        CancellationTokenSource reading;
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Interlocked.Exchange(ref consuming, 1) != 0) throw new InvalidOperationException("A viewer has one frame consumer.");
            reading = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        }
        using var consumerLifetime = reading;
        try
        {
            while (true)
            {
                ProtocolMessage message;
                try { message = await connection.ReadAsync(reading.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (reading.IsCancellationRequested) { yield break; }
                catch (EndOfStreamException) { yield break; }
                catch (IOException) when (lifetime.IsCancellationRequested) { yield break; }
                catch (ObjectDisposedException) when (reading.IsCancellationRequested) { yield break; }
                if (message.Type == MessageType.Disconnect) yield break;
                if (message.Type == MessageType.Heartbeat) continue;
                if (message.Type is MessageType.ControlGranted or MessageType.ControlRevoked)
                {
                    lock (lifecycle)
                    {
                        if (message.Type == MessageType.ControlGranted && !requestPending)
                            throw new InvalidDataException("Host granted control without a pending request.");
                        requestPending = false;
                        ChangeControlLocked(message.Type == MessageType.ControlGranted, notifyEvenIfUnchanged: message.Type == MessageType.ControlRevoked);
                    }
                    continue;
                }
                if (message.Type != MessageType.Frame) throw new InvalidDataException("Host sent an unsupported message.");
                yield return ProtocolCodec.DecodeFrame(message.Payload);
            }
        }
        finally { await DisconnectAsync().ConfigureAwait(false); }
    }

    public Task RequestControlAsync(CancellationToken token = default)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var now = Stopwatch.GetTimestamp();
            if (HasControl || requestPending || requestedControl && Stopwatch.GetElapsedTime(lastControlRequest, now) < TimeSpan.FromSeconds(1))
                throw new InvalidOperationException("Control requests are limited to one pending request and one per second.");
            requestedControl = true; lastControlRequest = now; requestPending = true;
            return SendAsync(MessageType.RequestControl, [], token);
        }
    }

    public Task ReleaseControlAsync(CancellationToken token = default)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestPending = false;
            ChangeControlLocked(false);
            return SendAsync(MessageType.ReleaseControl, [], token);
        }
    }

    public Task SendInputAsync(RemoteInputCommand command, CancellationToken token = default)
    {
        var payload = ProtocolCodec.EncodeInput(command);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!HasControl) throw new InvalidOperationException("The host has not granted control.");
            return SendAsync(MessageType.RemoteInput, payload, token);
        }
    }

    private async Task SendAsync(MessageType type, byte[] payload, CancellationToken token)
    {
        // Called under lifecycle so token registration cannot race source disposal.
        using var sending = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        try { await connection.WriteAsync(type, payload, sending.Token).ConfigureAwait(false); }
        catch { lifetime.Cancel(); throw; }
    }

    private void ChangeControlLocked(bool value, bool notifyEvenIfUnchanged = false)
    {
        if (value && disposed) return;
        var changed = (hasControl != 0) != value;
        Volatile.Write(ref hasControl, value ? 1 : 0);
        if (changed || notifyEvenIfUnchanged)
        {
            try { ControlChanged?.Invoke(value); }
            catch { client.Dispose(); }
        }
    }

    private void OnLifetimeEnded()
    {
        lock (lifecycle) { requestPending = false; ChangeControlLocked(false); }
        client.Dispose();
    }

    private async Task SendHeartbeatsAsync()
    {
        try { await connection.HeartbeatsAsync(lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch
        {
            // End an otherwise-blocked frame read when keepalive writes fail.
            lifetime.Cancel();
        }
    }

    public Task DisconnectAsync()
    {
        lock (lifecycle)
        {
            if (disconnect != null) return disconnect;
            disposed = true;
            requestPending = false; ChangeControlLocked(false);
            return disconnect = DisconnectCoreAsync();
        }
    }

    private async Task DisconnectCoreAsync()
    {
        // Publish the shared task and release lifecycle before joining cancellation
        // callbacks, which may themselves need lifecycle to clear control state.
        await Task.Yield();
        try
        {
            if (!lifetime.IsCancellationRequested)
                await connection.WriteAsync(MessageType.Disconnect, [], lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or TimeoutException or ObjectDisposedException) { }
        finally
        {
            lifetime.Cancel();
            client.Dispose();
            await heartbeats.ConfigureAwait(false);
            try { await secure.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                lifetimeClosed.Dispose();
                // Existing readers own linked sources; the lifecycle gate prevents
                // new registrations and the heartbeat worker has already finished.
                lifetime.Dispose();
            }
        }
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
