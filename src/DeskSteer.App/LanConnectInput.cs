using System.Diagnostics;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DeskSteer.Core;
using DeskSteer.Transport;

namespace DeskSteer.App;

public partial class LanConnectView
{
    private LanViewer? activeViewer;
    private CancellationTokenSource? inputLifetime;
    private Channel<RemoteInputCommand>? inputQueue;
    private Task? inputPump;
    private Action<bool>? controlChanged;
    private Window? inputWindow;
    private bool forwarding;
    private long lastMove;
    internal Func<MouseEventArgs, Point>? PointerPosition { get; set; }

    private void InitializeInputEvents()
    {
        Loaded += (_, _) =>
        {
            inputWindow = Window.GetWindow(this);
            if (inputWindow != null) inputWindow.Deactivated += WindowDeactivated;
        };
        Unloaded += (_, _) =>
        {
            if (inputWindow != null) inputWindow.Deactivated -= WindowDeactivated;
            inputWindow = null;
        };
    }

    private void InitializeInputPipeline(LanViewer viewer, CancellationToken token)
    {
        activeViewer = viewer;
        inputLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        inputQueue = Channel.CreateBounded<RemoteInputCommand>(new BoundedChannelOptions(32)
        { SingleReader = false, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        controlChanged = enabled => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(activeViewer, viewer))
            {
                diagnostics.Record(enabled ? DiagnosticEvent.ControlGranted : DiagnosticEvent.ControlRevoked);
                SetControlState(enabled, true);
            }
        }));
        viewer.ControlChanged += controlChanged;
        SetControlState(false, true);
        inputPump = PumpInputAsync(viewer, inputQueue.Reader, inputLifetime.Token);
    }

    private async Task PumpInputAsync(LanViewer viewer, ChannelReader<RemoteInputCommand> queue, CancellationToken token)
    {
        try
        {
            await foreach (var command in queue.ReadAllAsync(token).ConfigureAwait(false))
                if (viewer.HasControl) await viewer.SendInputAsync(command, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            diagnostics.Record(DiagnosticEvent.InputFailed);
            await Dispatcher.InvokeAsync(() => ReleaseInputAsync()).Task.Unwrap().ConfigureAwait(false);
        }
    }

    private async Task StopInputPipelineAsync()
    {
        var viewer = activeViewer;
        if (viewer != null && controlChanged != null) viewer.ControlChanged -= controlChanged;
        inputLifetime?.Cancel(); inputQueue?.Writer.TryComplete();
        if (inputPump != null) await inputPump.ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            forwarding = false; activeViewer = null; controlChanged = null;
            inputLifetime?.Dispose(); inputLifetime = null; inputQueue = null; inputPump = null;
            SetControlState(false, false);
        });
    }

    private void SetControlState(bool enabled, bool connected)
    {
        forwarding = enabled;
        RequestControlButton.IsEnabled = connected && !enabled;
        ReleaseControlButton.IsEnabled = connected && enabled;
        if (!enabled && inputQueue != null) while (inputQueue.Reader.TryRead(out _)) { }
        ControlStatus.Text = enabled
            ? "CONTROL APPROVED — click the shared screen to send keyboard and mouse input. Leaving the window releases control."
            : "Viewing only. Control is unavailable, declined or released; a new request needs host approval.";
    }

    private async void RequestControlClick(object sender, RoutedEventArgs e)
    {
        var viewer = activeViewer;
        if (viewer == null) return;
        RequestControlButton.IsEnabled = false;
        diagnostics.Record(DiagnosticEvent.ControlRequested);
        ControlStatus.Text = "Waiting for separate keyboard and mouse approval…";
        try { await viewer.RequestControlAsync(inputLifetime?.Token ?? default); }
        catch (Exception) { SetControlState(false, activeViewer != null); }
    }

    private async void ReleaseControlClick(object sender, RoutedEventArgs e) => await ReleaseInputAsync();
    private async void WindowDeactivated(object? sender, EventArgs e) { if (forwarding) await ReleaseInputAsync(); }
    private async void SharedScreenLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) { if (forwarding) await ReleaseInputAsync(); }

    private async Task ReleaseInputAsync()
    {
        var viewer = activeViewer;
        SetControlState(false, viewer != null);
        try { if (viewer != null) await viewer.ReleaseControlAsync(inputLifetime?.Token ?? default); }
        catch (Exception) { diagnostics.Record(DiagnosticEvent.LanSessionFailed); cancellation?.Cancel(); }
    }

    private void QueueInput(RemoteInputCommand command)
    {
        if (!forwarding || activeViewer?.HasControl != true) return;
        if (inputQueue?.Writer.TryWrite(command) != true) _ = ReleaseInputAsync();
    }

    internal static (int X, int Y)? MapPreviewPoint(double x, double y, double width, double height, int pixelsWide, int pixelsHigh)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || pixelsWide < 1 || pixelsHigh < 1) return null;
        var scale = Math.Min(width / pixelsWide, height / pixelsHigh);
        var shownWidth = pixelsWide * scale; var shownHeight = pixelsHigh * scale;
        if (!double.IsFinite(scale) || !double.IsFinite(shownWidth) || !double.IsFinite(shownHeight) || scale <= 0 || shownWidth <= 0 || shownHeight <= 0) return null;
        var left = (width - shownWidth) / 2; var top = (height - shownHeight) / 2;
        if (x < left || y < top || x > left + shownWidth || y > top + shownHeight) return null;
        var normalizedX = Math.Clamp(Math.Round((x - left) / shownWidth * 65535), 0, 65535);
        var normalizedY = Math.Clamp(Math.Round((y - top) / shownHeight * 65535), 0, 65535);
        return ((int)normalizedX, (int)normalizedY);
    }

    private (int X, int Y)? Pointer(MouseEventArgs e)
    {
        if (Preview.Source is not BitmapSource image) return null;
        var point = PointerPosition?.Invoke(e) ?? e.GetPosition(Preview);
        return MapPreviewPoint(point.X, point.Y, Preview.ActualWidth, Preview.ActualHeight, image.PixelWidth, image.PixelHeight);
    }

    private void SharedScreenMouseMove(object sender, MouseEventArgs e)
    {
        if (!forwarding || !Preview.IsKeyboardFocusWithin) return;
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(lastMove, now) < TimeSpan.FromMilliseconds(33)) return;
        lastMove = now;
        if (Pointer(e) is { } point) QueueInput(new(RemoteInputKind.Move, point.X, point.Y));
    }
    private void SharedScreenMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!forwarding || Pointer(e) is not { } point) return;
        var button = ButtonCode(e.ChangedButton); if (button == 0) return;
        Preview.Focus(); e.Handled = true;
        QueueInput(new(RemoteInputKind.ButtonDown, point.X, point.Y, button));
    }
    private void SharedScreenMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!forwarding) return;
        var button = ButtonCode(e.ChangedButton); if (button == 0) return;
        if (Pointer(e) is { } point) { e.Handled = true; QueueInput(new(RemoteInputKind.ButtonUp, point.X, point.Y, button)); }
        else _ = ReleaseInputAsync();
    }
    private void SharedScreenMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!forwarding || !Preview.IsKeyboardFocusWithin || Pointer(e) is not { } point) return;
        var delta = Math.Clamp(e.Delta / 120, -10, 10) * 120;
        if (delta == 0) return;
        e.Handled = true; QueueInput(new(RemoteInputKind.Wheel, point.X, point.Y, Delta: delta));
    }
    private async void SharedScreenMouseLeave(object sender, MouseEventArgs e)
    {
        if (forwarding && (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
            await ReleaseInputAsync();
    }
    private void SharedScreenKeyDown(object sender, KeyEventArgs e) => ForwardKey(e, true);
    private void SharedScreenKeyUp(object sender, KeyEventArgs e) => ForwardKey(e, false);
    private void ForwardKey(KeyEventArgs e, bool down)
    {
        if (!forwarding || !Preview.IsKeyboardFocusWithin) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var code = KeyInterop.VirtualKeyFromKey(key);
        if (!RemoteInputPolicy.IsAllowedKey(code)) return;
        e.Handled = true;
        QueueInput(new(down ? RemoteInputKind.KeyDown : RemoteInputKind.KeyUp, Code: code));
    }
    private static int ButtonCode(MouseButton button) => button switch { MouseButton.Left => 1, MouseButton.Right => 2, MouseButton.Middle => 3, _ => 0 };
}
