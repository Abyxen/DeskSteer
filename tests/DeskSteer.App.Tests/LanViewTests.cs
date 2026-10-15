using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using DeskSteer.Core;
using DeskSteer.Transport;
using Xunit;
using Page = DeskSteer.Core.Page;

namespace DeskSteer.App.Tests;

public sealed class LanViewTests
{
    [Fact]
    public Task InvalidHostAddressOrPortNeverOpensSelection() => WpfHost.RunAsync(async () =>
    {
        var selection = new ControlledSelection();
        var view = new LanHostView(selection, new Diagnostics(), (_, _) => Task.FromResult(true));
        foreach (var (address, port) in new[] { ("8.8.8.8", "47731"), ("0.0.0.0", "47731"), ("::1", "47731"), ("localhost", "47731"), ("127.0.0.1", "0"), ("127.0.0.1", "65536"), ("127.0.0.1", "invalid") })
        {
            Field<TextBox>(view, "AddressInput").Text = address;
            Field<TextBox>(view, "PortInput").Text = port;
            await view.StartAsync(new Window());
            Assert.Contains("private IPv4", view.StatusText);
            Assert.Equal(0, selection.SelectionCount);
            Assert.Equal(Visibility.Collapsed, Field<FrameworkElement>(view, "DetailsPanel").Visibility);
            Assert.True(Field<Button>(view, "StartButton").IsEnabled);
        }
        await view.StopAsync();
    });

    [Fact]
    public Task StopDuringSelectionDisposesLateResultWithoutOpeningListener() => WpfHost.RunAsync(async () =>
    {
        var selection = new ControlledSelection();
        var capture = new OwnedPixels();
        var view = new LanHostView(selection, new Diagnostics(), (_, _) => Task.FromResult(true));
        var port = FreePort();
        ConfigureHost(view, port);
        var selecting = view.StartAsync(new Window());
        Assert.False(Field<Button>(view, "StartButton").IsEnabled);
        await view.StopAsync();
        selection.Complete(capture);
        await selecting;
        Assert.Equal(1, capture.DisposeCount);
        Assert.Equal(0, capture.EnumerationCount);
        AssertDetailsCleared(view);
        Assert.True(Field<Button>(view, "StartButton").IsEnabled);
        await AssertListenerClosed(port);
    });

    [Fact]
    public Task DeclinedUiHostNeverEnumeratesSelectedCaptureAndClearsDetails() => WpfHost.RunAsync(async () =>
    {
        var selection = new ControlledSelection();
        var capture = new OwnedPixels();
        var approvedRequests = 0;
        var view = new LanHostView(selection, new Diagnostics(), (_, _) => { approvedRequests++; return Task.FromResult(false); });
        var port = FreePort(); ConfigureHost(view, port);
        try
        {
            var selecting = view.StartAsync(new Window()); selection.Complete(capture); await selecting;
            Assert.Equal(Visibility.Visible, Field<FrameworkElement>(view, "DetailsPanel").Visibility);
            var request = ReadPrivateConnectionDetails(view, port);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await Assert.ThrowsAsync<AuthenticationException>(async () => { await using var viewer = await LanViewer.ConnectAsync(request, deadline.Token); });
            await WaitFor(() => capture.DisposeCount == 1);
            Assert.Equal(1, approvedRequests);
            Assert.Equal(0, capture.EnumerationCount);
            AssertDetailsCleared(view);
            Assert.True(Field<Button>(view, "StartButton").IsEnabled);
            await AssertListenerClosed(port);
        }
        finally { await view.StopAsync(); }
    });

    [Fact]
    public Task ViewerDisconnectClearsPixelsSecretAndControlsAndReleasesHost() => WpfHost.RunAsync(async () =>
    {
        var selection = new ControlledSelection();
        var capture = new OwnedPixels();
        var host = new LanHostView(selection, new Diagnostics(), (_, _) => Task.FromResult(true));
        var viewer = new LanConnectView(new Diagnostics());
        var port = FreePort(); ConfigureHost(host, port);
        try
        {
            var selecting = host.StartAsync(new Window()); selection.Complete(capture); await selecting;
            ConfigureViewer(viewer, ReadPrivateConnectionDetails(host, port));
            await viewer.ConnectAsync();
            Assert.Equal(0, Field<PasswordBox>(viewer, "PairingCodeInput").Password.Length);
            await WaitFor(() => viewer.PreviewSource != null);
            Assert.Equal(Visibility.Collapsed, Field<StackPanel>(viewer, "ConnectionInputs").Visibility);
            Assert.Contains("VIEWING ACTIVE", viewer.StatusText);
            Assert.False(Field<Button>(viewer, "ConnectButton").IsEnabled);
            Assert.True(Field<Button>(viewer, "DisconnectButton").IsEnabled);
            Assert.Equal(1, capture.EnumerationCount);
            await viewer.StopAsync();
            Assert.Null(viewer.PreviewSource);
            Assert.Equal("", Field<TextBlock>(viewer, "ViewerStatistics").Text);
            Assert.Equal(0, Field<PasswordBox>(viewer, "PairingCodeInput").Password.Length);
            Assert.True(Field<Button>(viewer, "ConnectButton").IsEnabled);
            Assert.False(Field<Button>(viewer, "DisconnectButton").IsEnabled);
            Assert.Equal(Visibility.Visible, Field<StackPanel>(viewer, "ConnectionInputs").Visibility);
            await WaitFor(() => capture.DisposeCount == 1);
            AssertDetailsCleared(host);
            await AssertListenerClosed(port);
        }
        finally { await viewer.StopAsync(); await host.StopAsync(); }
    });

    [Fact]
    public Task DefaultApprovalDialogFitsMaximumLengthNameAndDeclineButton() => WpfHost.RunAsync(async () =>
    {
        var selection = new ControlledSelection();
        var capture = new OwnedPixels();
        var host = new LanHostView(selection, new Diagnostics());
        var owner = new Window { Title = "DeskSteer — owned approval fixture", Width = 820, Height = 620, Content = host };
        Window? prompt = null;
        owner.Show();
        var port = FreePort(); ConfigureHost(host, port);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var selecting = host.StartAsync(owner); selection.Complete(capture); await selecting;
            var request = ReadPrivateConnectionDetails(host, port) with { ViewerName = new string('W', 64) };
            var connecting = LanViewer.ConnectAsync(request, deadline.Token);
            await WaitFor(() => Application.Current.Windows.OfType<Window>().Any(window => window.Title == "Approve screen viewer"));
            prompt = Application.Current.Windows.OfType<Window>().Single(window => window.Title == "Approve screen viewer");
            await WaitFor(() => prompt.IsLoaded);
            prompt.UpdateLayout();
            var scroll = Assert.IsType<ScrollViewer>(prompt.Content);
            var body = Assert.IsType<StackPanel>(scroll.Content);
            var decline = body.Children.OfType<Button>().Single(button => Equals(button.Content, "Decline"));
            var text = body.Children.OfType<TextBlock>().Single();
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.Equal(SizeToContent.Height, prompt.SizeToContent);
            Assert.InRange(scroll.ScrollableHeight, 0, 1);
            var declineBounds = decline.TransformToAncestor(scroll).TransformBounds(new Rect(decline.RenderSize));
            Assert.True(declineBounds.Top >= 0 && declineBounds.Bottom <= scroll.ActualHeight, "The decline action must be visible for the maximum accepted viewer-name length.");
            decline.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.ThrowsAsync<AuthenticationException>(async () => { await using var viewer = await connecting; });
            await WaitFor(() => capture.DisposeCount == 1);
            Assert.Equal(0, capture.EnumerationCount);
        }
        finally { deadline.Cancel(); if (prompt?.IsVisible == true) prompt.Close(); await host.StopAsync(); owner.Close(); }
    });

    [Fact]
    public Task LeavingHostPageStopsListenerAndDisposesUnapprovedCapture() => WithMainWindow(async (window, selection) =>
    {
        await window.NavigateToAsync(Page.Host);
        var host = ((StackPanel)window.FindName("PageContent")).Children.OfType<LanHostView>().Single();
        var capture = new OwnedPixels();
        var port = FreePort(); ConfigureHost(host, port);
        var selecting = host.StartAsync(window); selection.Complete(capture); await selecting;
        await window.NavigateToAsync(Page.Home);
        Assert.Equal("A closer connection.", ((TextBlock)window.FindName("Heading")).Text);
        Assert.Equal(1, capture.DisposeCount);
        Assert.Equal(0, capture.EnumerationCount);
        AssertDetailsCleared(host);
        await AssertListenerClosed(port);
    });

    [Fact]
    public Task ClosingMainWindowStopsListenerAndDisposesUnapprovedCapture() => WithMainWindow(async (window, selection) =>
    {
        window.Show();
        await WaitFor(() => window.IsLoaded);
        await window.NavigateToAsync(Page.Host);
        var host = ((StackPanel)window.FindName("PageContent")).Children.OfType<LanHostView>().Single();
        var capture = new OwnedPixels();
        var port = FreePort(); ConfigureHost(host, port);
        var selecting = host.StartAsync(window); selection.Complete(capture); await selecting;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(6));
        Assert.Equal(1, capture.DisposeCount);
        Assert.Equal(0, capture.EnumerationCount);
        AssertDetailsCleared(host);
        await AssertListenerClosed(port);
    });

    [Fact]
    public Task LeavingConnectPageDisconnectsRealViewerAndClearsPixels() => WithMainWindow(async (window, _) =>
    {
        var selection = new ControlledSelection();
        var capture = new OwnedPixels();
        var host = new LanHostView(selection, new Diagnostics(), (_, _) => Task.FromResult(true));
        var port = FreePort(); ConfigureHost(host, port);
        try
        {
            var selecting = host.StartAsync(window); selection.Complete(capture); await selecting;
            await window.NavigateToAsync(Page.Connect);
            var viewer = ((StackPanel)window.FindName("PageContent")).Children.OfType<LanConnectView>().Single();
            ConfigureViewer(viewer, ReadPrivateConnectionDetails(host, port));
            await viewer.ConnectAsync();
            await WaitFor(() => viewer.PreviewSource != null);
            await window.NavigateToAsync(Page.Home);
            Assert.Null(viewer.PreviewSource);
            Assert.Equal(0, Field<PasswordBox>(viewer, "PairingCodeInput").Password.Length);
            Assert.True(Field<Button>(viewer, "ConnectButton").IsEnabled);
            await WaitFor(() => capture.DisposeCount == 1);
            await AssertListenerClosed(port);
        }
        finally { await host.StopAsync(); }
    });

    // Every source below consists solely of pixels owned by the test. Actual TCP/TLS,
    // JPEG encoding, WPF views and session lifecycle remain production implementations.
    private sealed class OwnedPixels : IScreenCapture
    {
        public int EnumerationCount { get; private set; }
        public int DisposeCount { get; private set; }
        public string DisplayName => "test-owned pixels";
        public CaptureMetrics Metrics => new(1, 0, 0, 1);
        public async IAsyncEnumerable<ScreenFrame> FramesAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            EnumerationCount++;
            yield return new ScreenFrame(2, 1, [20, 160, 50, 255, 20, 160, 50, 255], TimeSpan.Zero);
            await Task.Delay(Timeout.Infinite, token);
        }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
    private sealed class ControlledSelection : ICaptureSelection
    {
        private readonly TaskCompletionSource<IScreenCapture?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsSupported => true;
        public int SelectionCount { get; private set; }
        public Task<IScreenCapture?> SelectAsync(Window owner, CancellationToken cancellationToken) { SelectionCount++; return result.Task; }
        public void Complete(IScreenCapture? capture) => result.SetResult(capture);
    }
    private static Task WithMainWindow(Func<MainWindow, ControlledSelection, Task> action) => WpfHost.RunAsync(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "DeskSteerLanViewTests", Guid.NewGuid().ToString("N"));
        var selection = new ControlledSelection();
        var window = new MainWindow(new NavigationState(), new PreferencesStore(Path.Combine(directory, "preferences.json")), new Diagnostics(), NullLogger<MainWindow>.Instance, new NoDestination(), selection);
        try { await window.InitializeAsync(); await action(window, selection); }
        finally
        {
            window.Close();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    });
    private sealed class NoDestination : IDiagnosticDestination { public string? ChoosePath(Window owner) => null; }
    private static T Field<T>(FrameworkElement view, string name) where T : class => (T)view.FindName(name);
    private static void ConfigureHost(LanHostView host, int port)
    {
        Field<TextBox>(host, "AddressInput").Text = "127.0.0.1";
        Field<TextBox>(host, "PortInput").Text = port.ToString();
    }
    private static ConnectionRequest ReadPrivateConnectionDetails(LanHostView host, int port) => new("127.0.0.1", port, Field<TextBox>(host, "FingerprintText").Text, Field<TextBox>(host, "PairingCodeText").Text, "UI integration test viewer");
    private static void ConfigureViewer(LanConnectView viewer, ConnectionRequest request)
    {
        Field<TextBox>(viewer, "AddressInput").Text = request.Address;
        Field<TextBox>(viewer, "PortInput").Text = request.Port.ToString();
        Field<TextBox>(viewer, "FingerprintInput").Text = request.Fingerprint;
        Field<PasswordBox>(viewer, "PairingCodeInput").Password = request.PairingCode;
        Field<TextBox>(viewer, "ViewerNameInput").Text = request.ViewerName;
    }
    private static void AssertDetailsCleared(LanHostView host)
    {
        Assert.Equal(Visibility.Collapsed, Field<FrameworkElement>(host, "DetailsPanel").Visibility);
        Assert.Equal(0, Field<TextBox>(host, "EndpointText").Text.Length);
        Assert.Equal(0, Field<TextBox>(host, "FingerprintText").Text.Length);
        Assert.Equal(0, Field<TextBox>(host, "PairingCodeText").Text.Length);
    }
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
    private static async Task AssertListenerClosed(int port)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback, port, deadline.Token).AsTask());
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(15, deadline.Token);
    }
}
