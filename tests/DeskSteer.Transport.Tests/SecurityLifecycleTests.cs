using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using DeskSteer.Core;
using Xunit;

namespace DeskSteer.Transport.Tests;

public sealed class SecurityLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerSessionExpiryClosesSocketWithoutAnActiveFrameRead(bool consumeFirstFrame)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var hostOptions = new LanOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(40),
            ReadWriteTimeout = TimeSpan.FromSeconds(4),
            SessionLifetime = TimeSpan.FromSeconds(10)
        };
        var viewerOptions = hostOptions with { SessionLifetime = TimeSpan.FromMilliseconds(250) };
        await using var host = new LanHost(IPAddress.Loopback, options: hostOptions);
        var run = host.RunAsync((_, _) => Task.FromResult(true), FrameThenIdle, deadline.Token);
        await using var viewer = await LanViewer.ConnectAsync(LoopbackTlsTests.Request(host), deadline.Token, viewerOptions);
        if (consumeFirstFrame)
        {
            await using var frames = viewer.FramesAsync(deadline.Token).GetAsyncEnumerator();
            Assert.True(await frames.MoveNextAsync());
            // Leave the iterator suspended at its yield; expiry cannot depend on
            // another MoveNextAsync or on disposing this enumerator.
            await run.WaitAsync(TimeSpan.FromSeconds(2), deadline.Token);
        }
        else
        {
            // The viewer has connected but has never created a frame enumerator.
            await run.WaitAsync(TimeSpan.FromSeconds(2), deadline.Token);
        }
        Assert.Equal(LanSessionState.Closed, host.State);
        Assert.False(deadline.IsCancellationRequested);
        await viewer.DisconnectAsync();
    }

    [Theory]
    [InlineData("viewer\u202ename")]
    [InlineData("viewer\u2066name")]
    [InlineData("viewer\u2069name")]
    [InlineData("viewer\u200bname")]
    [InlineData("viewer\u2028name")]
    [InlineData("viewer\u2029name")]
    public void AuthenticationRejectsInvisibleFormattingAndLineSeparatorsInEitherDirection(string name)
    {
        Assert.Throws<InvalidDataException>(() => ProtocolCodec.EncodeAuthentication(new byte[32], name));
        var bytes = Encoding.UTF8.GetBytes(name);
        var payload = new byte[32 + bytes.Length];
        bytes.CopyTo(payload, 32);
        Assert.Throws<InvalidDataException>(() => ProtocolCodec.DecodeAuthentication(payload));
    }

    [Fact]
    public void AuthenticationRejectsInvalidUtf16ButPreservesValidEmoji()
    {
        foreach (var name in new[] { "viewer\ud800", "\udc00viewer", "viewer\ud800!" })
            Assert.Throws<InvalidDataException>(() => ProtocolCodec.EncodeAuthentication(new byte[32], name));
        const string valid = "Viewer \U0001f642";
        var decoded = ProtocolCodec.DecodeAuthentication(ProtocolCodec.EncodeAuthentication(new byte[32], valid));
        Assert.Equal(valid, decoded.Hello.DisplayName);
    }

    [Fact]
    public async Task InvalidViewerNameIsRejectedBeforeConnectingOrInvokingApproval()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        await using var host = new LanHost(IPAddress.Loopback);
        var approvals = 0;
        var run = host.RunAsync((_, _) => { Interlocked.Increment(ref approvals); return Task.FromResult(true); }, FrameThenIdle, deadline.Token);
        var request = LoopbackTlsTests.Request(host) with { ViewerName = "viewer\u202ename" };
        await Assert.ThrowsAsync<InvalidDataException>(async () => { await using var _ = await LanViewer.ConnectAsync(request, deadline.Token); });
        Assert.Equal(0, approvals);
        Assert.Equal(LanSessionState.Listening, host.State);
        deadline.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async IAsyncEnumerable<EncodedFrame> FrameThenIdle([EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        yield return FrameFixture.Create();
        await Task.Delay(Timeout.Infinite, token);
    }
}
