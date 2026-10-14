namespace RemoteDesktop.Core;

public sealed record ScreenFrame(int Width, int Height, byte[] BgraPixels, TimeSpan SystemRelativeTime);
public sealed record CaptureMetrics(long CapturedFrames, long DroppedFrames, long Resizes, double FramesPerSecond);

public interface IScreenCapture : IAsyncDisposable
{
    string DisplayName { get; }
    CaptureMetrics Metrics { get; }
    IAsyncEnumerable<ScreenFrame> FramesAsync(CancellationToken cancellationToken = default);
}
