using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using RemoteDesktop.Core;
using Xunit;

namespace RemoteDesktop.Transport.Tests;

public sealed class BackpressureTests
{
    [Fact]
    public async Task ApprovedViewerThatNeverReadsTriggersWriteDeadlineAndReleasesProducerAndListener()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var options = new LanOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(60),
            ReadWriteTimeout = TimeSpan.FromMilliseconds(800),
            SessionLifetime = TimeSpan.FromSeconds(30)
        };
        var large = LargeValidFrame();
        // APP segments enlarge the transport payload while retaining the real
        // fixture's baseline JPEG image and its bounded 2 x 1 dimensions.
        Assert.InRange(ProtocolCodec.EncodeFrame(large).Length, 1024 * 1024, ProtocolCodec.MaxPayloadLength);
        var producerDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var produced = 0;
        async IAsyncEnumerable<EncodedFrame> Produce([EnumeratorCancellation] CancellationToken token)
        {
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    yield return large with { TimestampTicks = Interlocked.Increment(ref produced) };
                    await Task.Yield();
                }
            }
            finally { producerDisposed.TrySetResult(); }
        }

        await using var host = new LanHost(IPAddress.Loopback, options: options);
        var run = host.RunAsync((_, _) => Task.FromResult(true), Produce, deadline.Token);
        await using var viewer = await LanViewer.ConnectAsync(LoopbackTlsTests.Request(host), deadline.Token, options);
        // This is the real viewer and TLS stack. Its outbound keepalives continue,
        // distinguishing receive backpressure from a missing-heartbeat timeout.
        // Never call FramesAsync: the socket receive buffer must eventually fill.
        var error = await Assert.ThrowsAsync<TimeoutException>(() => run.WaitAsync(TimeSpan.FromSeconds(10), deadline.Token));
        Assert.Contains("receive a complete message", error.Message);
        Assert.Equal(LanSessionState.Failed, host.State);
        Assert.False(deadline.IsCancellationRequested);
        Assert.True(producerDisposed.Task.IsCompletedSuccessfully);
        Assert.True(Volatile.Read(ref produced) >= 1);
        var countAfterStop = Volatile.Read(ref produced);
        await Task.Delay(100, deadline.Token);
        Assert.Equal(countAfterStop, Volatile.Read(ref produced));

        await host.DisposeAsync();
        using var connectionAfterStop = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => connectionAfterStop.ConnectAsync(host.Endpoint.Address, host.Endpoint.Port, deadline.Token).AsTask());
        await viewer.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(2), deadline.Token);
    }

    private static EncodedFrame LargeValidFrame()
    {
        var original = FrameFixture.Create();
        const int segmentLength = ushort.MaxValue;
        const int segments = 16;
        using var output = new MemoryStream();
        output.Write(original.Jpeg.AsSpan(0, 2));
        var segment = new byte[segmentLength + 2];
        segment[0] = 0xff; segment[1] = 0xef; // JPEG APP15.
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), segmentLength);
        for (var index = 0; index < segments; index++) output.Write(segment);
        output.Write(original.Jpeg.AsSpan(2));
        return original with { Jpeg = output.ToArray() };
    }
}
