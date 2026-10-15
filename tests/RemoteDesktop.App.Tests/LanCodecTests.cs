using System.IO;
using System.Buffers.Binary;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RemoteDesktop.Capture;
using RemoteDesktop.Core;
using RemoteDesktop.Transport;
using Xunit;

namespace RemoteDesktop.App.Tests;

public sealed class LanCodecTests
{
    [Fact]
    public Task JpegRoundTripPreservesDimensionsTimeAndApproximateColors() => WpfHost.RunAsync(() =>
    {
        var pixels = new byte[16 * 8 * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        { pixels[offset] = 50; pixels[offset + 1] = 160; pixels[offset + 2] = 20; pixels[offset + 3] = 255; }
        var encoded = JpegFrameCodec.Encode(new ScreenFrame(16, 8, pixels, TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks, encoded.TimestampTicks);
        Assert.Equal((16, 8), JpegFrameCodec.ReadDimensions(encoded.Jpeg));
        var decoded = JpegFrameCodec.Decode(encoded);
        Assert.True(decoded.IsFrozen);
        Assert.Equal(16, decoded.PixelWidth);
        Assert.Equal(8, decoded.PixelHeight);
        AssertGreenPixel(decoded);
        return Task.CompletedTask;
    });

    [Fact]
    public Task JpegDecoderRejectsMetadataMismatchOversizeAndMalformedHeaders() => WpfHost.RunAsync(() =>
    {
        var valid = JpegFrameCodec.Encode(new ScreenFrame(2, 1, new byte[8], TimeSpan.Zero));
        Assert.Throws<InvalidDataException>(() => JpegFrameCodec.Decode(valid with { Width = 3 }));
        Assert.Throws<InvalidDataException>(() => JpegFrameCodec.Decode(valid with { Width = 1281 }));
        Assert.Throws<InvalidDataException>(() => JpegFrameCodec.Decode(valid with { Jpeg = new byte[JpegFrameCodec.MaxBytes + 1] }));
        Assert.Throws<InvalidDataException>(() => JpegFrameCodec.Decode(valid with { Jpeg = [255, 216, 255, 217] }));
        Assert.Throws<InvalidDataException>(() => JpegFrameCodec.ReadDimensions([255, 216, 255, 224, 0, 1]));
        Assert.Throws<InvalidDataException>(() => JpegFrameCodec.Encode(new ScreenFrame(2, 1, new byte[4], default)));
        return Task.CompletedTask;
    });

    [Fact]
    public Task DuplicateJpegFrameHeadersAreRejectedBeforeImageDecode() => WpfHost.RunAsync(() =>
    {
        var valid = JpegFrameCodec.Encode(new ScreenFrame(2, 1, new byte[8], TimeSpan.Zero));
        var position = Enumerable.Range(0, valid.Jpeg.Length - 1).First(index => valid.Jpeg[index] == 255 && valid.Jpeg[index + 1] == 192);
        var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(valid.Jpeg.AsSpan(position + 2)) + 2;
        var duplicated = new byte[valid.Jpeg.Length + segmentLength];
        valid.Jpeg.AsSpan(0, position + segmentLength).CopyTo(duplicated);
        valid.Jpeg.AsSpan(position, segmentLength).CopyTo(duplicated.AsSpan(position + segmentLength));
        valid.Jpeg.AsSpan(position + segmentLength).CopyTo(duplicated.AsSpan(position + segmentLength * 2));
        Assert.Throws<InvalidDataException>(() => JpegFrameCodec.Decode(valid with { Jpeg = duplicated }));
        return Task.CompletedTask;
    });

    [InteractiveCaptureFact]
    public Task OwnedWindowsCapturePassesThroughJpegActualTlsAndViewerDecoder() => WpfHost.RunAsync(async () =>
    {
        var fixture = new Window
        {
            Title = "Project Remote — owned LAN pipeline test", Width = 480, Height = 300,
            Background = new SolidColorBrush(Color.FromRgb(20, 160, 50))
        };
        fixture.Show();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            await using var capture = new WindowsScreenCapture(OwnedCaptureItem.Create(fixture));
            await using var host = new LanHost(IPAddress.Loopback);
            var approved = false;
            var run = host.RunAsync((_, _) => { approved = true; return Task.FromResult(true); }, token => EncodeFrames(capture, token), lifetime.Token);
            await using var viewer = await LanViewer.ConnectAsync(new("127.0.0.1", host.Endpoint.Port, host.Fingerprint, host.PairingCode, "Owned test viewer"), lifetime.Token);
            await using var frames = viewer.FramesAsync(lifetime.Token).GetAsyncEnumerator();
            Assert.True(await frames.MoveNextAsync());
            Assert.True(approved);
            var decoded = JpegFrameCodec.Decode(frames.Current);
            Assert.True(decoded.IsFrozen);
            Assert.InRange(decoded.PixelWidth, 1, 1280);
            Assert.InRange(decoded.PixelHeight, 1, 720);
            AssertGreenPixel(decoded);
            await viewer.DisconnectAsync();
            await run.WaitAsync(lifetime.Token);
        }
        finally { lifetime.Cancel(); fixture.Close(); }
    });

    private static async IAsyncEnumerable<EncodedFrame> EncodeFrames(IScreenCapture capture, [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var frame in capture.FramesAsync(token).ConfigureAwait(false)) yield return JpegFrameCodec.Encode(frame);
    }
    private static void AssertGreenPixel(BitmapSource decoded)
    {
        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(decoded.PixelWidth / 2, decoded.PixelHeight / 2, 1, 1), pixel, 4, 0);
        Assert.InRange(pixel[0], 40, 60);
        Assert.InRange(pixel[1], 150, 170);
        Assert.InRange(pixel[2], 10, 30);
    }
}
