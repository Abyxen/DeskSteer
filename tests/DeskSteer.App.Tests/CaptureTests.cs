using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSteer.Capture;
using DeskSteer.Core;
using Xunit;

namespace DeskSteer.App.Tests;

public sealed class InteractiveCaptureFactAttribute : FactAttribute
{
    public InteractiveCaptureFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DESKSTEER_CAPTURE_TESTS") != "1")
            Skip = "Requires an interactive Windows graphics desktop. Set DESKSTEER_CAPTURE_TESTS=1 to capture only a disposable test-owned window.";
    }
}

public sealed class CaptureTests
{
    [Fact]
    public void ScalerPreservesAspectRatioBoundsAndPixels()
    {
        var pixels = new byte[1920 * 1080 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 50; pixels[i + 1] = 160; pixels[i + 2] = 20; pixels[i + 3] = 255; }
        var scaled = FrameScaler.Scale(1920, 1080, pixels, TimeSpan.FromSeconds(1));
        Assert.Equal(1280, scaled.Width); Assert.Equal(720, scaled.Height);
        Assert.Equal(new byte[] { 50, 160, 20, 255 }, scaled.BgraPixels.Take(4));
        Assert.Equal(TimeSpan.FromSeconds(1), scaled.SystemRelativeTime);
        Assert.Throws<ArgumentException>(() => FrameScaler.Scale(20, 20, new byte[4], default));
        Assert.Throws<NotSupportedException>(() => WindowsScreenCapture.ValidateSize(new() { Width = 10000, Height = 10000 }));
    }

    [InteractiveCaptureFact]
    public Task RealGraphicsCaptureReceivesPixelsResizesAndRestarts() => WpfHost.RunAsync(async () =>
    {
        var fixture = new Window
        {
            Title = "DeskSteer — disposable graphics test", Width = 640, Height = 400,
            Background = new SolidColorBrush(Color.FromRgb(20, 160, 50)),
            Content = new TextBlock { Text = "DeskSteer\nOWNED CAPTURE TEST\nNo personal data", Foreground = Brushes.White, FontSize = 28, Margin = new Thickness(30) }
        };
        fixture.Show();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await using (var capture = new WindowsScreenCapture(OwnedCaptureItem.Create(fixture)))
            {
                await using var frames = capture.FramesAsync(timeout.Token).GetAsyncEnumerator();
                Assert.True(await frames.MoveNextAsync());
                var first = frames.Current;
                Assert.Contains(Enumerable.Range(0, first.BgraPixels.Length / 4), index => first.BgraPixels[index * 4] == 50 && first.BgraPixels[index * 4 + 1] == 160 && first.BgraPixels[index * 4 + 2] == 20);
                SaveEvidence(first, "captured-owned-window.png");
                fixture.Width = 820; fixture.Height = 500;
                ScreenFrame next;
                do { Assert.True(await frames.MoveNextAsync(), $"No resized frame. Metrics={capture.Metrics}; fixture={fixture.ActualWidth}x{fixture.ActualHeight}"); next = frames.Current; } while (next.Width == first.Width && next.Height == first.Height);
                Assert.True(capture.Metrics.Resizes >= 1);
                Assert.True(capture.Metrics.CapturedFrames >= 2);
                SaveEvidence(next, "captured-resized-window.png");
                timeout.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => frames.MoveNextAsync().AsTask());
            }
            using var restartTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var restarted = new WindowsScreenCapture(OwnedCaptureItem.Create(fixture));
            await using var restartedFrames = restarted.FramesAsync(restartTimeout.Token).GetAsyncEnumerator();
            Assert.True(await restartedFrames.MoveNextAsync());
            Assert.True(restartedFrames.Current.BgraPixels.Length > 0);
        }
        finally { fixture.Close(); }
    });

    [InteractiveCaptureFact]
    public Task DisposingCaptureBeforeFirstFrameAndDuringEnumerationIsSafe() => WpfHost.RunAsync(async () =>
    {
        Assert.True(new CapturePicker().IsSupported);
        var fixture = new Window { Title = "DeskSteer — disposal test", Width = 400, Height = 260, Background = Brushes.DarkSlateBlue };
        fixture.Show();
        try
        {
            var unused = new WindowsScreenCapture(OwnedCaptureItem.Create(fixture));
            await Task.WhenAll(unused.DisposeAsync().AsTask(), unused.DisposeAsync().AsTask());
            await using (var rejected = unused.FramesAsync().GetAsyncEnumerator())
                await Assert.ThrowsAsync<ObjectDisposedException>(() => rejected.MoveNextAsync().AsTask());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var active = new WindowsScreenCapture(OwnedCaptureItem.Create(fixture));
            await using var frames = active.FramesAsync(timeout.Token).GetAsyncEnumerator();
            Assert.True(await frames.MoveNextAsync());
            await Task.WhenAll(active.DisposeAsync().AsTask(), active.DisposeAsync().AsTask());
            // Disposing the enumerator after its source must not cancel an already-disposed CTS.
            await frames.DisposeAsync();
        }
        finally { fixture.Close(); }
    });

    [InteractiveCaptureFact]
    public Task PreviewClearsOnStopAndCanRestart() => WpfHost.RunAsync(async () =>
    {
        var fixture = new Window { Title = "DeskSteer — preview test", Width = 480, Height = 300, Background = Brushes.DarkCyan };
        fixture.Show();
        var view = new CaptureView(new OwnedSelection(fixture), new Diagnostics());
        try
        {
            await view.SelectAsync(fixture);
            await WaitFor(() => view.PreviewSource != null);
            Assert.Contains("CAPTURE ACTIVE", view.StatusText);
            await view.StopAsync();
            Assert.Null(view.PreviewSource);
            await view.SelectAsync(fixture);
            await WaitFor(() => view.PreviewSource != null);
            await view.StopAsync();
            Assert.Null(view.PreviewSource);
        }
        finally { await view.StopAsync(); fixture.Close(); }
    });

    [InteractiveCaptureFact]
    public Task ClosingOwnedTargetClearsPreviewAndStopsCleanly() => WpfHost.RunAsync(async () =>
    {
        var fixture = new Window { Title = "DeskSteer — target close test", Width = 400, Height = 260, Background = Brushes.DarkGreen };
        fixture.Show();
        var view = new CaptureView(new OwnedSelection(fixture), new Diagnostics());
        try
        {
            await view.SelectAsync(fixture);
            await WaitFor(() => view.PreviewSource != null);
            fixture.Close();
            await WaitFor(() => view.PreviewSource == null);
            Assert.DoesNotContain("CAPTURE ACTIVE", view.StatusText);
            // Stop must observe a successful preview task, including natural target closure.
            await view.StopAsync();
        }
        finally { await view.StopAsync(); fixture.Close(); }
    });

    [InteractiveCaptureFact]
    public Task SlowConsumerDropsOldFramesAndReceivesLatestPixels() => WpfHost.RunAsync(async () =>
    {
        var fixture = new Window { Title = "DeskSteer — bounded queue test", Width = 400, Height = 260, Background = Brushes.DarkGreen };
        fixture.Show();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var capture = new WindowsScreenCapture(OwnedCaptureItem.Create(fixture));
            await using var frames = capture.FramesAsync(timeout.Token).GetAsyncEnumerator();
            Assert.True(await frames.MoveNextAsync());
            var firstTime = frames.Current.SystemRelativeTime;
            // Keep producing changed frames while the consumer deliberately does not read.
            for (var index = 0; index < 8; index++)
            {
                fixture.Background = index % 2 == 0 ? Brushes.DarkBlue : Brushes.DarkRed;
                await Task.Delay(160, timeout.Token);
            }
            fixture.Background = new SolidColorBrush(Color.FromRgb(23, 145, 67));
            var beforeFinal = capture.Metrics.CapturedFrames;
            await WaitFor(() => capture.Metrics.CapturedFrames > beforeFinal);
            await Task.Delay(500, timeout.Token);
            Assert.True(capture.Metrics.DroppedFrames >= 1);
            Assert.True(await frames.MoveNextAsync());
            var latest = frames.Current;
            Assert.True(latest.SystemRelativeTime > firstTime);
            Assert.Contains(Enumerable.Range(0, latest.BgraPixels.Length / 4), index => latest.BgraPixels[index * 4] == 67 && latest.BgraPixels[index * 4 + 1] == 145 && latest.BgraPixels[index * 4 + 2] == 23);
            timeout.Cancel();
        }
        finally { fixture.Close(); }
    });
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(25, timeout.Token);
    }
    private static void SaveEvidence(ScreenFrame frame, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("DESKSTEER_EVIDENCE_DIR");
        if (directory == null) return;
        Directory.CreateDirectory(directory);
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.BgraPixels, frame.Width * 4);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, fileName)); png.Save(stream);
    }
    private sealed class OwnedSelection(Window fixture) : ICaptureSelection
    {
        public bool IsSupported => true;
        public Task<IScreenCapture?> SelectAsync(Window owner, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IScreenCapture?>(new WindowsScreenCapture(OwnedCaptureItem.Create(fixture)));
        }
    }
}
