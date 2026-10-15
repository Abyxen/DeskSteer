using System.IO;
using System.Windows;
using System.Windows.Controls;
using RemoteDesktop.Core;
using RemoteDesktop.Transport;

namespace RemoteDesktop.App;

public partial class LanConnectView : UserControl
{
    private readonly Diagnostics diagnostics;
    private CancellationTokenSource? cancellation;
    private Task? connectionTask;
    private bool stopping;
    internal string StatusText => ConnectionStatus.Text;
    internal System.Windows.Media.ImageSource? PreviewSource => Preview.Source;
    public LanConnectView(Diagnostics diagnostics) { InitializeComponent(); this.diagnostics = diagnostics; }
    private async void ConnectClick(object sender, RoutedEventArgs e) => await ConnectAsync();
    internal async Task ConnectAsync()
    {
        if (stopping || connectionTask is { IsCompleted: false }) return;
        if (!int.TryParse(PortInput.Text, out var port) || port is < 1 or > 65535 || string.IsNullOrWhiteSpace(AddressInput.Text) || string.IsNullOrWhiteSpace(FingerprintInput.Text) || string.IsNullOrWhiteSpace(PairingCodeInput.Password) || string.IsNullOrWhiteSpace(ViewerNameInput.Text))
        { ConnectionStatus.Text = "Enter all connection details supplied by the host."; return; }
        await StopAsync();
        var request = new ConnectionRequest(AddressInput.Text.Trim(), port, FingerprintInput.Text.Trim(), PairingCodeInput.Password.Trim(), ViewerNameInput.Text.Trim());
        PairingCodeInput.Clear();
        var source = new CancellationTokenSource(); cancellation = source;
        ConnectButton.IsEnabled = false; DisconnectButton.IsEnabled = true;
        connectionTask = RunConnectionAsync(request, source);
    }
    private async Task RunConnectionAsync(ConnectionRequest request, CancellationTokenSource source)
    {
        ConnectionStatus.Text = "Connecting securely and waiting for host approval…";
        var elapsed = new System.Diagnostics.Stopwatch(); long received = 0;
        try
        {
            await using var viewer = await LanViewer.ConnectAsync(request, source.Token);
            diagnostics.Record(DiagnosticEvent.LanViewerConnected);
            ConnectionStatus.Text = "Connected — waiting for the first shared frame…";
            await foreach (var frame in viewer.FramesAsync(source.Token).ConfigureAwait(false))
            {
                var bitmap = await Task.Run(() => JpegFrameCodec.Decode(frame), source.Token).ConfigureAwait(false);
                if (received == 0) elapsed.Start();
                received++;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (source.IsCancellationRequested || !ReferenceEquals(cancellation, source)) return;
                    ConnectionInputs.Visibility = Visibility.Collapsed;
                    var rate = received < 2 ? "measuring FPS" : $"{(received - 1) / Math.Max(0.001, elapsed.Elapsed.TotalSeconds):F1} received FPS";
                    Preview.Source = bitmap; ViewerStatistics.Text = $"{frame.Width} × {frame.Height} · {rate} · {frame.Jpeg.Length:N0} JPEG bytes"; ConnectionStatus.Text = "● VIEWING ACTIVE — viewing only. Disconnect ends the session.";
                }).Task.ConfigureAwait(false);
            }
            await Dispatcher.InvokeAsync(() => { if (ReferenceEquals(cancellation, source)) ConnectionStatus.Text = "The host ended sharing."; });
        }
        catch (OperationCanceledException) { await Dispatcher.InvokeAsync(() => ConnectionStatus.Text = "Disconnected."); }
        catch (Exception)
        { diagnostics.Record(DiagnosticEvent.LanSessionFailed); await Dispatcher.InvokeAsync(() => ConnectionStatus.Text = "Connection ended. Verify the host details, approval and network availability."); }
        finally
        {
            diagnostics.Record(DiagnosticEvent.LanViewerStopped);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(cancellation, source)) return;
                cancellation = null; source.Dispose(); Preview.Source = null; ViewerStatistics.Text = "";
                ConnectButton.IsEnabled = true; DisconnectButton.IsEnabled = false;
                ConnectionInputs.Visibility = Visibility.Visible;
                PairingCodeInput.Clear();
            });
        }
    }
    private async void DisconnectClick(object sender, RoutedEventArgs e) => await StopAsync();
    public async Task StopAsync()
    {
        if (stopping) { if (connectionTask != null) await connectionTask; return; }
        stopping = true; ConnectButton.IsEnabled = false; DisconnectButton.IsEnabled = false;
        cancellation?.Cancel();
        if (connectionTask != null) await connectionTask;
        connectionTask = null; cancellation?.Dispose(); cancellation = null;
        Preview.Source = null; ViewerStatistics.Text = ""; ConnectionStatus.Text = "Disconnected.";
        ConnectionInputs.Visibility = Visibility.Visible;
        stopping = false; ConnectButton.IsEnabled = true;
    }
}



