using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using RemoteDesktop.Core;
using RemoteDesktop.Transport;

namespace RemoteDesktop.App;

public partial class LanHostView : UserControl
{
    private readonly ICaptureSelection selection;
    private readonly Diagnostics diagnostics;
    private readonly Func<ViewerHello, CancellationToken, Task<bool>> approve;
    private CancellationTokenSource? cancellation;
    private Task? sessionTask;
    private bool selecting;
    private bool stopping;
    internal string StatusText => HostStatus.Text;
    public LanHostView(ICaptureSelection selection, Diagnostics diagnostics) : this(selection, diagnostics, null) { }
    internal LanHostView(ICaptureSelection selection, Diagnostics diagnostics, Func<ViewerHello, CancellationToken, Task<bool>>? approval)
    {
        InitializeComponent(); this.selection = selection; this.diagnostics = diagnostics; approve = approval ?? ApproveAsync;
        StartButton.IsEnabled = selection.IsSupported;
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
        HostStatus.Text = "Choose a window or display to share…";
        IScreenCapture? capture = null;
        try
        {
            var window = owner ?? Window.GetWindow(this) ?? throw new InvalidOperationException("An owner window is required.");
            capture = await selection.SelectAsync(window, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (capture == null) { HostStatus.Text = "Selection cancelled. No network sharing is active."; return; }
            var host = new LanHost(address, port);
            EndpointText.Text = host.Endpoint.ToString(); FingerprintText.Text = host.Fingerprint;
            PairingCodeText.Text = host.PairingCode;
            ExpiryText.Text = $"Connection details expire at {host.ExpiresAt.ToLocalTime():t}. One viewer per session.";
            DetailsPanel.Visibility = Visibility.Visible;
            var owned = capture; capture = null;
            diagnostics.Record(DiagnosticEvent.LanHostStarted);
            sessionTask = RunSessionAsync(host, owned, source);
        }
        catch (OperationCanceledException) { HostStatus.Text = "Sharing stopped."; }
        catch (Exception)
        { HostStatus.Text = "Could not start sharing. Check the address, port and Windows capture permissions."; }
        finally
        {
            if (capture != null) await DisposeCaptureAsync(capture);
            selecting = false;
            if (sessionTask == null || sessionTask.IsCompleted)
            {
                if (ReferenceEquals(cancellation, source)) cancellation = null;
                source.Dispose(); StopButton.IsEnabled = false; StartButton.IsEnabled = selection.IsSupported; AddressInput.IsEnabled = true; PortInput.IsEnabled = true;
            }
        }
    }
    private async Task RunSessionAsync(LanHost host, IScreenCapture capture, CancellationTokenSource source)
    {
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
        HostStatus.Text = "Waiting for a viewer. Sharing begins only after your approval.";
        try
        {
            await host.RunAsync(async (hello, token) =>
            {
                var accepted = await approve(hello, token);
                diagnostics.Record(accepted ? DiagnosticEvent.LanRequestApproved : DiagnosticEvent.LanRequestDeclined);
                return accepted;
            }, token => EncodeFrames(capture, token), source.Token);
            if (!source.IsCancellationRequested) HostStatus.Text = "Sharing ended. Start a new session to share again.";
        }
        catch (OperationCanceledException) { HostStatus.Text = "Sharing stopped."; }
        catch (Exception)
        { diagnostics.Record(DiagnosticEvent.LanSessionFailed); HostStatus.Text = "Sharing ended. The request was declined, expired, disconnected or could not be authenticated."; }
        finally
        {
            diagnostics.Record(DiagnosticEvent.LanHostStopped);
            host.StateChanged -= StateChanged;
            try { await host.DisposeAsync(); } catch (Exception) { diagnostics.Record(DiagnosticEvent.LanSessionFailed); }
            await DisposeCaptureAsync(capture);
            if (ReferenceEquals(cancellation, source))
            {
                cancellation = null; source.Dispose();
                StopButton.IsEnabled = false; StartButton.IsEnabled = selection.IsSupported; AddressInput.IsEnabled = true; PortInput.IsEnabled = true;
                ClearDetails();
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
    private async Task<bool> ShowApprovalAsync(ViewerHello hello, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        var prompt = new Window
        {
            Title = "Approve screen viewer", Width = 460, SizeToContent = SizeToContent.Height,
            MaxHeight = 650, ResizeMode = ResizeMode.NoResize, Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 38, 50)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(235, 239, 245)),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 14
        };
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = $"Viewer name (claimed):\n{hello.DisplayName}\n\nAllow this viewer to see your selected screen? They cannot control your keyboard or mouse.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,20) });
        var allow = new Button { Content = "Approve viewing" }; var decline = new Button { Content = "Decline", IsCancel = true };
        allow.Click += (_, _) => { result.TrySetResult(true); prompt.Close(); };
        decline.Click += (_, _) => { result.TrySetResult(false); prompt.Close(); };
        prompt.Closed += (_, _) => { closed = true; result.TrySetResult(false); };
        body.Children.Add(allow); body.Children.Add(decline);
        prompt.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 600 };
        using var registration = token.Register(() => Dispatcher.BeginInvoke(new Action(() => { result.TrySetCanceled(token); if (!closed) prompt.Close(); })));
        prompt.Show(); return await result.Task;
    }
    private async void StopClick(object sender, RoutedEventArgs e) => await StopAsync();
    public async Task StopAsync()
    {
        if (stopping) { if (sessionTask != null) await sessionTask; return; }
        stopping = true; StartButton.IsEnabled = false; StopButton.IsEnabled = false;
        cancellation?.Cancel();
        if (sessionTask != null) await sessionTask;
        sessionTask = null;
        if (!selecting) { cancellation?.Dispose(); cancellation = null; }
        ClearDetails(); HostStatus.Text = "No network sharing is active.";
        stopping = false; StartButton.IsEnabled = !selecting && selection.IsSupported; AddressInput.IsEnabled = !selecting; PortInput.IsEnabled = !selecting;
    }
    private async Task DisposeCaptureAsync(IScreenCapture capture)
    {
        try { await capture.DisposeAsync(); } catch (Exception) { diagnostics.Record(DiagnosticEvent.LanSessionFailed); }
    }
    private void ClearDetails() { DetailsPanel.Visibility = Visibility.Collapsed; EndpointText.Clear(); FingerprintText.Clear(); PairingCodeText.Clear(); ExpiryText.Text = ""; }
    internal static bool IsPrivateAddress(IPAddress address) => LanAddressPolicy.IsAllowed(address);
}



