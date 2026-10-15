using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using DeskSteer.Core;
using DeskSteer.Transport;

namespace DeskSteer.App;

public partial class LanHostView : UserControl
{
    private readonly ICaptureSelection selection;
    private readonly Diagnostics diagnostics;
    private readonly Func<ViewerHello, CancellationToken, Task<bool>> approve;
    private CancellationTokenSource? cancellation;
    private Task? sessionTask;
    private bool selecting;
    private bool stopping;
    private LanHost? activeHost;
    private SessionControlIndicator? controlIndicator;
    internal string StatusText => HostStatus.Text;
    public LanHostView(ICaptureSelection selection, Diagnostics diagnostics) : this(selection, diagnostics, null) { }
    internal LanHostView(ICaptureSelection selection, Diagnostics diagnostics, Func<ViewerHello, CancellationToken, Task<bool>>? approval)
    {
        InitializeComponent(); this.selection = selection; this.diagnostics = diagnostics; approve = approval ?? ApproveAsync;
        StartButton.IsEnabled = selection.IsSupported;
        ControlMode.IsEnabled = selection is IDesktopControlSelection;
    }
    private async void StartClick(object sender, RoutedEventArgs e) => await StartAsync();
    internal async Task StartAsync(Window? owner = null)
    {
        if (selecting || stopping || !selection.IsSupported || sessionTask is { IsCompleted: false }) return;
        if (!IPAddress.TryParse(AddressInput.Text.Trim(), out var address) || !LanAddressPolicy.IsAllowed(address) || !int.TryParse(PortInput.Text, out var port) || port is < 1 or > 65535)
        { HostStatus.Text = "Enter this computer's private IPv4 address and a port from 1 to 65535."; return; }
        await StopAsync();
        var source = new CancellationTokenSource(); cancellation = source;
        selecting = true; StartButton.IsEnabled = false; StopButton.IsEnabled = true; AddressInput.IsEnabled = false; PortInput.IsEnabled = false;
        ControlMode.IsEnabled = false;
        HostStatus.Text = "Choose a window or display to share…";
        IScreenCapture? capture = null;
        IRemoteInputSink? input = null;
        try
        {
            var window = owner ?? Window.GetWindow(this) ?? throw new InvalidOperationException("An owner window is required.");
            if (ControlMode.IsChecked == true && selection is IDesktopControlSelection controlled)
            {
                var desktop = await controlled.SelectPrimaryDisplayAsync(window, source.Token);
                capture = desktop.Capture; input = desktop.Input;
            }
            else capture = await selection.SelectAsync(window, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (capture == null) { HostStatus.Text = "Selection cancelled. No network sharing is active."; return; }
            var host = new LanHost(address, port);
            EndpointText.Text = host.Endpoint.ToString(); FingerprintText.Text = host.Fingerprint;
            PairingCodeText.Text = host.PairingCode;
            ExpiryText.Text = $"Connection details expire at {host.ExpiresAt.ToLocalTime():t}. One viewer per session.";
            DetailsPanel.Visibility = Visibility.Visible;
            var owned = capture; capture = null;
            diagnostics.Record(DiagnosticEvent.LanHostStarted);
            sessionTask = RunSessionAsync(host, owned, source, input);
            input = null;
        }
        catch (OperationCanceledException) { HostStatus.Text = "Sharing stopped."; }
        catch (Exception)
        { HostStatus.Text = "Could not start sharing. Check the address, port and Windows capture permissions."; }
        finally
        {
            if (capture != null) await DisposeCaptureAsync(capture);
            try { input?.ReleaseAll(); }
            catch (Exception) { diagnostics.Record(DiagnosticEvent.LanSessionFailed); }
            selecting = false;
            if (sessionTask == null || sessionTask.IsCompleted)
            {
                if (ReferenceEquals(cancellation, source)) cancellation = null;
                source.Dispose(); StopButton.IsEnabled = false; StartButton.IsEnabled = selection.IsSupported; AddressInput.IsEnabled = true; PortInput.IsEnabled = true;
                ControlMode.IsEnabled = selection is IDesktopControlSelection;
            }
        }
    }
    private async Task RunSessionAsync(LanHost host, IScreenCapture capture, CancellationTokenSource source, IRemoteInputSink? input)
    {
        activeHost = host;
        ControlMode.IsEnabled = false;
        void ControlChanged(bool enabled) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(activeHost, host)) return;
            diagnostics.Record(enabled ? DiagnosticEvent.ControlGranted : DiagnosticEvent.ControlRevoked);
            SetControlIndicator(enabled);
        }));
        void StateChanged(LanSessionState state) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(cancellation, source)) return;
            HostStatus.Text = state switch
            {
                LanSessionState.Listening => "Waiting for a viewer. Sharing begins only after your approval.",
                LanSessionState.Authenticating => "Checking the viewer's connection details…",
                LanSessionState.AwaitingApproval => "A viewer is waiting for your approval.",
                LanSessionState.Streaming => "● SHARING ACTIVE — viewing only. Stop sharing ends the session.",
                _ => HostStatus.Text
            };
        }));
        host.StateChanged += StateChanged;
        host.ControlChanged += ControlChanged;
        HostStatus.Text = "Waiting for a viewer. Sharing begins only after your approval.";
        try
        {
            await host.RunAsync(async (hello, token) =>
            {
                var accepted = await approve(hello, token);
                diagnostics.Record(accepted ? DiagnosticEvent.LanRequestApproved : DiagnosticEvent.LanRequestDeclined);
                return accepted;
            }, token => EncodeFrames(capture, token), source.Token, input == null ? null : ApproveControlAsync, input);
            if (!source.IsCancellationRequested) HostStatus.Text = "Sharing ended. Start a new session to share again.";
        }
        catch (OperationCanceledException) { HostStatus.Text = "Sharing stopped."; }
        catch (Exception)
        { diagnostics.Record(DiagnosticEvent.LanSessionFailed); HostStatus.Text = "Sharing ended. The request was declined, expired, disconnected or could not be authenticated."; }
        finally
        {
            diagnostics.Record(DiagnosticEvent.LanHostStopped);
            host.StateChanged -= StateChanged;
            host.ControlChanged -= ControlChanged;
            try { await host.DisposeAsync(); } catch (Exception) { diagnostics.Record(DiagnosticEvent.LanSessionFailed); }
            if (ReferenceEquals(activeHost, host)) { activeHost = null; SetControlIndicator(false); }
            await DisposeCaptureAsync(capture);
            if (ReferenceEquals(cancellation, source))
            {
                cancellation = null; source.Dispose();
                StopButton.IsEnabled = false; StartButton.IsEnabled = selection.IsSupported; AddressInput.IsEnabled = true; PortInput.IsEnabled = true;
                ClearDetails();
                ControlMode.IsEnabled = selection is IDesktopControlSelection;
            }
        }
    }
    private static async IAsyncEnumerable<EncodedFrame> EncodeFrames(IScreenCapture capture, [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var frame in capture.FramesAsync(token).ConfigureAwait(false))
            yield return await Task.Run(() => JpegFrameCodec.Encode(frame), token).ConfigureAwait(false);
    }
    private async Task<bool> ApproveAsync(ViewerHello hello, CancellationToken token)
    {
        // Modeless approval keeps Stop responsive. Transport timeout/cancellation closes the prompt.
        var operation = await Dispatcher.InvokeAsync(() => ShowApprovalAsync(hello, token));
        return await operation;
    }
    private async Task<bool> ApproveControlAsync(ViewerHello hello, CancellationToken token)
    {
        diagnostics.Record(DiagnosticEvent.ControlRequested);
        var operation = await Dispatcher.InvokeAsync(() => ShowControlApprovalAsync(hello, token));
        var accepted = await operation;
        if (accepted)
            await Dispatcher.InvokeAsync(() => { token.ThrowIfCancellationRequested(); SetControlIndicator(true); });
        else diagnostics.Record(DiagnosticEvent.ControlDeclined);
        return accepted;
    }
    private Task<bool> ShowControlApprovalAsync(ViewerHello hello, CancellationToken token) =>
        ShowPermissionAsync(hello, token, true);
    private async Task<bool> ShowApprovalAsync(ViewerHello hello, CancellationToken token)
        => await ShowPermissionAsync(hello, token, false);
    private async Task<bool> ShowPermissionAsync(ViewerHello hello, CancellationToken token, bool control)
    {
        token.ThrowIfCancellationRequested();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        var prompt = new Window
        {
            Title = control ? "Approve keyboard and mouse control" : "Approve screen viewer", Width = 460, SizeToContent = SizeToContent.Height,
            MaxHeight = 650, ResizeMode = ResizeMode.NoResize, Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 38, 50)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(235, 239, 245)),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 14
        };
        var body = new StackPanel { Margin = new Thickness(24) };
        var permission = control
            ? "Allow this viewer to use your keyboard and mouse? Pointer actions target the shared primary display; typing and shortcuts act on the foreground application on your computer. Allow only a trusted person. Use the on-screen Revoke control bar to stop input; viewing can continue."
            : "Allow this viewer to see your selected screen? Viewing alone does not allow keyboard or mouse control.";
        body.Children.Add(new TextBlock { Text = $"Viewer name (claimed):\n{hello.DisplayName}\n\n{permission}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,20) });
        var allow = new Button { Content = control ? "Approve keyboard and mouse" : "Approve viewing" }; var decline = new Button { Content = "Decline", IsCancel = true };
        allow.Click += (_, _) => { result.TrySetResult(true); prompt.Close(); };
        decline.Click += (_, _) => { result.TrySetResult(false); prompt.Close(); };
        prompt.Closed += (_, _) => { closed = true; result.TrySetResult(false); };
        body.Children.Add(allow); body.Children.Add(decline);
        prompt.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 600 };
        using var registration = token.Register(() => Dispatcher.BeginInvoke(new Action(() => { result.TrySetCanceled(token); if (!closed) prompt.Close(); })));
        prompt.Show(); return await result.Task;
    }
    private async void StopClick(object sender, RoutedEventArgs e) => await StopAsync();
    private async void RevokeClick(object sender, RoutedEventArgs e) => await RevokeControlAsync();
    private async Task RevokeControlAsync()
    {
        var host = activeHost;
        try { if (host != null) await host.RevokeControlAsync(); }
        catch (Exception) { diagnostics.Record(DiagnosticEvent.LanSessionFailed); await StopAsync(); }
        finally { SetControlIndicator(false); }
    }
    private void SetControlIndicator(bool enabled)
    {
        RevokeButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled)
        {
            HostStatus.Text = "● REMOTE CONTROL ACTIVE — keyboard and mouse approved.";
            if (controlIndicator == null)
            {
                controlIndicator = new SessionControlIndicator(RevokeControlAsync, StopAsync);
                controlIndicator.Show();
            }
        }
        else
        {
            var indicator = controlIndicator; controlIndicator = null; indicator?.Dismiss();
            if (activeHost?.State == LanSessionState.Streaming)
                HostStatus.Text = "● SHARING ACTIVE — viewing only. Remote control is off.";
        }
    }
    public async Task StopAsync()
    {
        if (stopping) { if (sessionTask != null) await sessionTask; return; }
        stopping = true; StartButton.IsEnabled = false; StopButton.IsEnabled = false;
        cancellation?.Cancel();
        if (sessionTask != null) await sessionTask;
        sessionTask = null;
        if (!selecting) { cancellation?.Dispose(); cancellation = null; }
        ClearDetails(); HostStatus.Text = "No network sharing is active.";
        SetControlIndicator(false);
        stopping = false; StartButton.IsEnabled = !selecting && selection.IsSupported; AddressInput.IsEnabled = !selecting; PortInput.IsEnabled = !selecting;
        ControlMode.IsEnabled = !selecting && selection is IDesktopControlSelection;
    }
    private async Task DisposeCaptureAsync(IScreenCapture capture)
    {
        try { await capture.DisposeAsync(); } catch (Exception) { diagnostics.Record(DiagnosticEvent.LanSessionFailed); }
    }
    private void ClearDetails() { DetailsPanel.Visibility = Visibility.Collapsed; EndpointText.Clear(); FingerprintText.Clear(); PairingCodeText.Clear(); ExpiryText.Text = ""; }
    internal static bool IsPrivateAddress(IPAddress address) => LanAddressPolicy.IsAllowed(address);
}



