using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DeskSteer.Core;
using Xunit;

namespace DeskSteer.Transport.Tests;

public sealed class ControlTlsTests
{
    [Fact]
    public async Task DecliningViewNeverRequestsControlOrAppliesInput()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var host = new LanHost(IPAddress.Loopback);
        var sink = new RecordingSink(); var controlApprovals = 0;
        var run = host.RunAsync((_, _) => Task.FromResult(false), Frames, deadline.Token,
            (_, _) => { Interlocked.Increment(ref controlApprovals); return Task.FromResult(true); }, sink);
        await Assert.ThrowsAsync<AuthenticationException>(async () => { await using var _ = await LanViewer.ConnectAsync(LoopbackTlsTests.Request(host), deadline.Token); });
        await run.WaitAsync(deadline.Token);
        Assert.Equal(0, controlApprovals); Assert.Empty(sink.Applied); Assert.False(host.HasControl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeniedOrUnavailableControlKeepsViewingAndNotifiesFalse(bool viewOnly)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var host = new LanHost(IPAddress.Loopback);
        var sink = new RecordingSink();
        var run = host.RunAsync((_, _) => Task.FromResult(true), Frames, deadline.Token,
            viewOnly ? null : (_, _) => Task.FromResult(false), sink);
        await using var viewer = await LanViewer.ConnectAsync(LoopbackTlsTests.Request(host), deadline.Token);
        var denied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewer.ControlChanged += value => { if (!value) denied.TrySetResult(); };
        var frameCount = 0;
        var pump = PumpAsync(viewer, () => Interlocked.Increment(ref frameCount), deadline.Token);
        await viewer.RequestControlAsync(deadline.Token);
        await denied.Task.WaitAsync(deadline.Token);
        Assert.False(viewer.HasControl); Assert.False(host.HasControl);
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewer.SendInputAsync(new(RemoteInputKind.KeyDown, Code: 65), deadline.Token));
        var before = Volatile.Read(ref frameCount);
        await Until(() => Volatile.Read(ref frameCount) > before, deadline.Token);
        Assert.Empty(sink.Applied);
        await viewer.DisconnectAsync(); await pump.WaitAsync(deadline.Token); await run.WaitAsync(deadline.Token);
    }

    [Fact]
    public async Task ApprovedCommandsReachSinkAndLocalRevokeReleasesBeforeReturningItsTask()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var host = new LanHost(IPAddress.Loopback);
        var sink = new RecordingSink(); var hostChanges = new ConcurrentQueue<bool>();
        host.ControlChanged += hostChanges.Enqueue;
        var run = host.RunAsync((_, _) => Task.FromResult(true), Frames, deadline.Token, (_, _) => Task.FromResult(true), sink);
        await using var viewer = await LanViewer.ConnectAsync(LoopbackTlsTests.Request(host), deadline.Token);
        var count = 0; var pump = PumpAsync(viewer, () => Interlocked.Increment(ref count), deadline.Token);
        await viewer.RequestControlAsync(deadline.Token);
        await Until(() => viewer.HasControl && host.HasControl, deadline.Token);
        var commands = new[]
        {
            new RemoteInputCommand(RemoteInputKind.Move, 32768, 12000),
            new RemoteInputCommand(RemoteInputKind.ButtonDown, 100, 200, 1),
            new RemoteInputCommand(RemoteInputKind.ButtonUp, 100, 200, 1),
            new RemoteInputCommand(RemoteInputKind.Wheel, 300, 400, Delta: -120),
            new RemoteInputCommand(RemoteInputKind.KeyDown, Code: 65)
        };
        foreach (var command in commands) await viewer.SendInputAsync(command, deadline.Token);
        await Until(() => sink.Applied.Count == commands.Length, deadline.Token);
        Assert.Equal(commands, sink.Applied.ToArray()); Assert.Equal(1, sink.HeldCount);
        var revoked = host.RevokeControlAsync();
        // Check before awaiting notification delivery: authority and native holds
        // must already be revoked even while the TLS write is still pending.
        Assert.False(host.HasControl); Assert.Equal(0, sink.HeldCount); Assert.True(sink.ReleaseCount >= 1);
        await revoked.WaitAsync(deadline.Token);
        await Until(() => !viewer.HasControl, deadline.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewer.SendInputAsync(new(RemoteInputKind.KeyDown, Code: 66), deadline.Token));
        var before = Volatile.Read(ref count);
        await Until(() => Volatile.Read(ref count) > before, deadline.Token);
        Assert.Equal(commands.Length, sink.Applied.Count); Assert.Equal(new[] { true, false }, hostChanges.ToArray());
        await viewer.DisconnectAsync(); await pump.WaitAsync(deadline.Token); await run.WaitAsync(deadline.Token);
    }

    [Fact]
    public async Task DisconnectClearsBothAuthoritiesAndReleasesHeldKeys()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var host = new LanHost(IPAddress.Loopback);
        var sink = new RecordingSink();
        var run = host.RunAsync((_, _) => Task.FromResult(true), Frames, deadline.Token, (_, _) => Task.FromResult(true), sink);
        await using var viewer = await LanViewer.ConnectAsync(LoopbackTlsTests.Request(host), deadline.Token);
        var pump = PumpAsync(viewer, () => { }, deadline.Token);
        await viewer.RequestControlAsync(deadline.Token); await Until(() => viewer.HasControl, deadline.Token);
        await viewer.SendInputAsync(new(RemoteInputKind.KeyDown, Code: 16), deadline.Token);
        await Until(() => sink.HeldCount == 1, deadline.Token);
        var closing = viewer.DisconnectAsync(); Assert.False(viewer.HasControl);
        await closing; await pump.WaitAsync(deadline.Token); await run.WaitAsync(deadline.Token);
        Assert.False(host.HasControl); Assert.Equal(0, sink.HeldCount); Assert.True(sink.ReleaseCount >= 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationOrTimeoutInvalidatesALateTrueControlApproval(bool timeout)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var options = new LanOptions { ApprovalTimeout = TimeSpan.FromMilliseconds(250) };
        await using var host = new LanHost(IPAddress.Loopback, options: options);
        var sink = new RecordingSink();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken approvalToken = default;
        var run = host.RunAsync((_, _) => Task.FromResult(true), Frames, deadline.Token,
            (_, token) => { approvalToken = token; entered.TrySetResult(); return answer.Task; }, sink);
        await using var viewer = await LanViewer.ConnectAsync(LoopbackTlsTests.Request(host), deadline.Token, options);
        var revoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var grants = 0;
        viewer.ControlChanged += value => { if (value) Interlocked.Increment(ref grants); else revoked.TrySetResult(); };
        var count = 0; var pump = PumpAsync(viewer, () => Interlocked.Increment(ref count), deadline.Token);
        await viewer.RequestControlAsync(deadline.Token); await entered.Task.WaitAsync(deadline.Token);
        if (!timeout) await host.RevokeControlAsync();
        await revoked.Task.WaitAsync(deadline.Token);
        Assert.True(approvalToken.IsCancellationRequested);
        answer.TrySetResult(true);
        var before = Volatile.Read(ref count); await Until(() => Volatile.Read(ref count) >= before + 2, deadline.Token);
        Assert.False(host.HasControl); Assert.False(viewer.HasControl); Assert.Equal(0, grants); Assert.Empty(sink.Applied);
        await viewer.DisconnectAsync(); await pump.WaitAsync(deadline.Token); await run.WaitAsync(deadline.Token);
    }

    [Theory]
    [InlineData("unauthorized")]
    [InlineData("malformed")]
    [InlineData("rate")]
    [InlineData("after revoke")]
    [InlineData("duplicate request")]
    public async Task UnauthorizedMalformedFloodedAndRevokedWireInputsEndSessionWithoutFurtherApplication(string attack)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var host = new LanHost(IPAddress.Loopback);
        var sink = new RecordingSink();
        var run = host.RunAsync((_, _) => Task.FromResult(true), Frames, deadline.Token, (_, _) => Task.FromResult(true), sink);
        await using var peer = await WirePeer.ConnectAsync(host, deadline.Token);
        if (attack != "unauthorized")
        {
            await peer.SendAsync(MessageType.RequestControl, [], deadline.Token);
            await peer.ReadUntilAsync(MessageType.ControlGranted, deadline.Token);
        }
        var before = sink.Applied.Count;
        if (attack == "after revoke") { await host.RevokeControlAsync(); Assert.False(host.HasControl); }
        var payload = ProtocolCodec.EncodeInput(new(RemoteInputKind.KeyDown, Code: 65));
        if (attack == "malformed") payload[3] = 99;
        try
        {
            if (attack == "duplicate request") await peer.SendAsync(MessageType.RequestControl, [], deadline.Token);
            else if (attack == "rate") await peer.SendBurstAsync(payload, 601, deadline.Token);
            else await peer.SendAsync(MessageType.RemoteInput, payload, deadline.Token);
        }
        catch (IOException) when (run.IsCompleted) { }
        await Assert.ThrowsAsync<InvalidDataException>(() => run.WaitAsync(deadline.Token));
        Assert.Equal(LanSessionState.Failed, host.State); Assert.False(host.HasControl);
        Assert.Equal(0, sink.HeldCount); Assert.True(sink.ReleaseCount >= 1);
        if (attack == "rate") Assert.InRange(sink.Applied.Count, 1, 400);
        else Assert.Equal(before, sink.Applied.Count);
    }

    private static async Task PumpAsync(LanViewer viewer, Action frame, CancellationToken token)
    { await foreach (var _ in viewer.FramesAsync(token)) frame(); }
    private static async Task Until(Func<bool> condition, CancellationToken token)
    { while (!condition()) await Task.Delay(10, token); }
    private static async IAsyncEnumerable<EncodedFrame> Frames([EnumeratorCancellation] CancellationToken token)
    {
        var frame = FrameFixture.Create();
        while (true) { token.ThrowIfCancellationRequested(); yield return frame; await Task.Delay(20, token); }
    }

    private sealed class RecordingSink : IRemoteInputSink
    {
        private readonly object gate = new(); private readonly HashSet<int> held = [];
        private int released;
        public ConcurrentQueue<RemoteInputCommand> Applied { get; } = new();
        public int ReleaseCount => Volatile.Read(ref released);
        public int HeldCount { get { lock (gate) return held.Count; } }
        public void Apply(RemoteInputCommand command)
        {
            lock (gate)
            {
                Applied.Enqueue(command);
                if (command.Kind == RemoteInputKind.KeyDown) held.Add(command.Code);
                if (command.Kind == RemoteInputKind.KeyUp) held.Remove(command.Code);
            }
        }
        public void ReleaseAll() { lock (gate) { held.Clear(); Interlocked.Increment(ref released); } }
    }

    // An independent wire peer deliberately bypasses the viewer API's local
    // permission/validation guard, exercising server enforcement over actual TLS.
    private sealed class WirePeer(TcpClient tcp, SslStream tls) : IAsyncDisposable
    {
        private uint send = 1, receive = 1;
        public static async Task<WirePeer> ConnectAsync(LanHost host, CancellationToken token)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(host.Endpoint.Address, host.Endpoint.Port, token);
            var pin = Convert.FromHexString(host.Fingerprint);
            var tls = new SslStream(tcp.GetStream(), false, (_, remote, _, errors) =>
            {
                if (remote == null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
                using var cert = X509CertificateLoader.LoadCertificate(remote.GetRawCertData());
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.RawData), pin)) return false;
                using var chain = new X509Chain(); chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(cert); chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                return chain.Build(cert);
            });
            try
            {
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "desksteer.local", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, token);
                var peer = new WirePeer(tcp, tls);
                await peer.SendAsync(MessageType.Authentication, ProtocolCodec.EncodeAuthentication(Convert.FromBase64String(host.PairingCode), "Recording sink attack fixture"), token);
                await peer.ReadUntilAsync(MessageType.Approved, token);
                return peer;
            }
            catch { await tls.DisposeAsync(); tcp.Dispose(); throw; }
        }
        public Task SendAsync(MessageType type, byte[] payload, CancellationToken token) => ProtocolCodec.WriteAsync(tls, new(type, send++, payload), token);
        public async Task SendBurstAsync(byte[] payload, int count, CancellationToken token)
        {
            using var bytes = new MemoryStream();
            for (var index = 0; index < count; index++) await ProtocolCodec.WriteAsync(bytes, new(MessageType.RemoteInput, send++, payload), token);
            await tls.WriteAsync(bytes.ToArray(), token);
        }
        public async Task ReadUntilAsync(MessageType type, CancellationToken token)
        {
            while ((await ProtocolCodec.ReadAsync(tls, receive++, token)).Type != type) { }
        }
        public async ValueTask DisposeAsync() { tcp.Dispose(); await tls.DisposeAsync(); }
    }
}
