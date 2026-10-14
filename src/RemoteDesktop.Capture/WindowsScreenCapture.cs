using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using RemoteDesktop.Core;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;

namespace RemoteDesktop.Capture;

public sealed class WindowsScreenCapture : IScreenCapture
{
    private readonly GraphicsCaptureItem item;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<ScreenFrame> frames;
    private readonly Stopwatch elapsed = new();
    private readonly object lifecycle = new();
    private Task? producer;
    private Task? disposal;
    private int started;
    private int disposed;
    private long captured, dropped, resizes;
    public string DisplayName => item.DisplayName;
    public CaptureMetrics Metrics
    {
        get
        {
            var count = Interlocked.Read(ref captured);
            var seconds = elapsed.Elapsed.TotalSeconds;
            return new(count, Interlocked.Read(ref dropped), Interlocked.Read(ref resizes), seconds > 0 ? count / seconds : 0);
        }
    }

    internal WindowsScreenCapture(GraphicsCaptureItem item)
    {
        this.item = item;
        frames = Channel.CreateBounded<ScreenFrame>(new BoundedChannelOptions(1)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest
        }, _ => Interlocked.Increment(ref dropped));
    }

    public async IAsyncEnumerable<ScreenFrame> FramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked;
        Task runningProducer;
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            if (started != 0) throw new InvalidOperationException("A capture has one consumer. Select again to restart.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            started = 1;
            // Publish the task before DisposeAsync can cancel or release the lifetime source.
            runningProducer = producer = Task.Run(() => CaptureAsync(linked.Token));
        }
        using var consumerLifetime = linked;
        try
        {
            await foreach (var frame in frames.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false)) yield return frame;
        }
        finally
        {
            // The local source remains alive even if the capture itself was already disposed.
            linked.Cancel();
            await runningProducer.ConfigureAwait(false);
        }
    }

    private async Task CaptureAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        var subscribed = false;
        void OnClosed(GraphicsCaptureItem _, object args)
        {
            lock (lifecycle)
            {
                if (disposed == 0) lifetime.Cancel();
            }
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            item.Closed += OnClosed;
            subscribed = true;
            elapsed.Start();
            ValidateSize(item.Size);
            cancellationToken.ThrowIfCancellationRequested();
            using var device = Direct3DDevice.Create();
            var size = item.Size;
            while (!cancellationToken.IsCancellationRequested)
            {
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            using var session = pool.CreateCaptureSession(item);
            var arrivals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
            void OnArrived(Direct3D11CaptureFramePool _, object args) => arrivals.Writer.TryWrite(true);
            pool.FrameArrived += OnArrived;
            try
            {
                // Keep the Windows capture border and cursor defaults. No permission bypass.
                session.StartCapture();
                await foreach (var _ in arrivals.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false); // cap processing at 10 FPS
                    var resized = false;
                    using (var frame = pool.TryGetNextFrame())
                    {
                        if (frame == null) continue;
                        var contentSize = frame.ContentSize;
                        if (contentSize.Width <= 0 || contentSize.Height <= 0) continue;
                        ValidateSize(contentSize);
                        if (contentSize.Width != size.Width || contentSize.Height != size.Height)
                        {
                            size = contentSize;
                            resized = true;
                        }
                        else
                        {
                            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore).AsTask(cancellationToken).ConfigureAwait(false);
                            var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
                            bitmap.CopyToBuffer(pixels.AsBuffer());
                            var preview = FrameScaler.Scale(bitmap.PixelWidth, bitmap.PixelHeight, pixels, frame.SystemRelativeTime);
                            cancellationToken.ThrowIfCancellationRequested();
                            frames.Writer.TryWrite(preview);
                            Interlocked.Increment(ref captured);
                        }
                    }
                    if (resized)
                    {
                        // A fresh pool/session guarantees the initial frame even for a static window.
                        // Tear down this generation after returning all checked-out buffers.
                        Interlocked.Increment(ref resizes);
                        break;
                    }
                }
            }
            finally { pool.FrameArrived -= OnArrived; }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { failure = error; }
        finally
        {
            elapsed.Stop();
            // Subscription failures must also reach the reader instead of leaving it waiting.
            try { if (subscribed) item.Closed -= OnClosed; }
            catch (Exception error) { failure ??= error; }
            frames.Writer.TryComplete(failure);
        }
    }
    internal static void ValidateSize(SizeInt32 size)
    {
        if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > 16_777_216)
            throw new NotSupportedException("The selected surface exceeds the capture limit of 16 megapixels or is unavailable.");
    }
    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            if (disposal != null) return new(disposal);
            disposed = 1;
            lifetime.Cancel();
            disposal = DisposeCoreAsync(producer);
            return new(disposal);
        }
    }

    private async Task DisposeCoreAsync(Task? runningProducer)
    {
        try
        {
            if (runningProducer != null) await runningProducer.ConfigureAwait(false);
        }
        finally
        {
            frames.Writer.TryComplete();
            // Only FramesAsync reads this single-reader channel. Its final buffered managed
            // frame is released with the capture object rather than drained by a second reader.
            lifetime.Dispose();
        }
    }
}
