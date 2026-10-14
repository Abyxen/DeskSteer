using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RemoteDesktop.Core;

namespace RemoteDesktop.App;

public partial class CaptureView : UserControl
{
    private readonly ICaptureSelection selection;
    private readonly Diagnostics diagnostics;
    private CancellationTokenSource? cancellation;
    private Task? previewTask;
    private Task? stopTask;
    private bool selecting;
    private bool stopping;
    private long generation;
    internal ImageSource? PreviewSource => Preview.Source;
    internal string StatusText => CaptureStatus.Text;

    public CaptureView(ICaptureSelection selection, Diagnostics diagnostics)
    {
        InitializeComponent(); this.selection = selection; this.diagnostics = diagnostics;
        SelectButton.IsEnabled = selection.IsSupported;
        if (!selection.IsSupported) CaptureStatus.Text = "Windows Graphics Capture is unavailable. Check your Windows version and graphics drivers.";
    }
    private async void SelectClick(object sender, RoutedEventArgs e) => await SelectAsync();
    internal async Task SelectAsync(Window? owner = null)
    {
        if (selecting || stopping || !selection.IsSupported) return;
        await StopAsync();
        selecting = true; SelectButton.IsEnabled = false; StopButton.IsEnabled = true;
        var request = ++generation;
        var source = new CancellationTokenSource(); cancellation = source;
        CaptureStatus.Text = "Choose a display or window in the Windows picker…";
        try
        {
            var captureOwner = owner ?? Window.GetWindow(this) ?? throw new InvalidOperationException("Capture needs an owner window.");
            var capture = await selection.SelectAsync(captureOwner, source.Token);
            if (capture == null) { CaptureStatus.Text = "Selection cancelled. No capture is active."; return; }
            if (source.IsCancellationRequested || request != generation) { await capture.DisposeAsync(); return; }
            diagnostics.Record(DiagnosticEvent.CaptureStarted);
            CaptureStatus.Text = "●  CAPTURE ACTIVE — local preview only";
            // Do not log or export the selected display/window title.
            previewTask = ShowPreviewAsync(capture, source.Token, request);
        }
        catch (OperationCanceledException) { CaptureStatus.Text = "Capture stopped."; }
        catch (Exception error) when (error is COMException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
        { diagnostics.Record(DiagnosticEvent.CaptureFailed); CaptureStatus.Text = "Could not start capture. Check Windows capture support, permissions and graphics drivers."; }
        finally
        {
            selecting = false;
            SelectButton.IsEnabled = !stopping && selection.IsSupported;
            if (previewTask == null || previewTask.IsCompleted)
            {
                if (ReferenceEquals(cancellation, source)) cancellation = null;
                source.Dispose(); StopButton.IsEnabled = false;
            }
        }
    }
    private async Task ShowPreviewAsync(IScreenCapture capture, CancellationToken token, long request)
    {
        await using (capture)
        {
            try
            {
                await foreach (var frame in capture.FramesAsync(token).ConfigureAwait(false))
                {
                    var image = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.BgraPixels, frame.Width * 4);
                    image.Freeze();
                    // Await dispatch: at most one UI update in flight; no unbounded Dispatcher queue.
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested || request != generation) return;
                        Preview.Source = image;
                        var metrics = capture.Metrics;
                        Statistics.Text = $"{frame.Width} × {frame.Height}  ·  {metrics.FramesPerSecond:F1} captured FPS  ·  {metrics.DroppedFrames} dropped  ·  latest-frame queue: 1";
                    }).Task.ConfigureAwait(false);
                }
            }
            // A source closing also cancels the engine's own token; that is a normal stop.
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is COMException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
            {
                diagnostics.Record(DiagnosticEvent.CaptureFailed);
                await Dispatcher.InvokeAsync(() => { if (request == generation) CaptureStatus.Text = "Capture ended. The window may be closed or protected, or the graphics device may be unavailable."; });
            }
            finally
            {
                diagnostics.Record(DiagnosticEvent.CaptureStopped);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (request != generation) return;
                    Preview.Source = null; Statistics.Text = ""; StopButton.IsEnabled = false;
                    if (CaptureStatus.Text.StartsWith("●")) CaptureStatus.Text = "Capture stopped. Select again to restart.";
                });
            }
        }
    }
    private async void StopClick(object sender, RoutedEventArgs e) => await StopAsync();
    public async Task StopAsync()
    {
        if (stopTask is { IsCompleted: false }) { await stopTask; return; }
        stopTask = StopCoreAsync();
        await stopTask;
    }
    private async Task StopCoreAsync()
    {
        stopping = true; SelectButton.IsEnabled = false; StopButton.IsEnabled = false;
        ++generation;
        cancellation?.Cancel();
        if (previewTask != null) await previewTask;
        previewTask = null;
        // A picker operation owns its CTS until its finally block completes.
        if (!selecting) { cancellation?.Dispose(); cancellation = null; }
        Preview.Source = null; Statistics.Text = "";
        CaptureStatus.Text = "No capture is active.";
        stopping = false; SelectButton.IsEnabled = !selecting && selection.IsSupported;
    }
}
