using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeskSteer.Core;
using Xunit;

namespace DeskSteer.App.Tests;

public sealed class RemoteInputViewTests
{
    [Fact]
    public void PointerMappingAccountsForLetterboxingAndMapsEdgesExactly()
    {
        Assert.Equal((0, 0), LanConnectView.MapPreviewPoint(0, 50, 200, 200, 200, 100));
        Assert.Equal((65535, 65535), LanConnectView.MapPreviewPoint(200, 150, 200, 200, 200, 100));
        Assert.Equal((32768, 32768), LanConnectView.MapPreviewPoint(100, 100, 200, 200, 200, 100));
        Assert.Null(LanConnectView.MapPreviewPoint(100, 49, 200, 200, 200, 100));
        Assert.Null(LanConnectView.MapPreviewPoint(100, 151, 200, 200, 200, 100));
        Assert.Equal((0, 0), LanConnectView.MapPreviewPoint(50, 0, 200, 200, 100, 200));
        Assert.Equal((65535, 65535), LanConnectView.MapPreviewPoint(150, 200, 200, 200, 100, 200));
        Assert.Null(LanConnectView.MapPreviewPoint(49, 100, 200, 200, 100, 200));
        Assert.Equal((32768, 32768), LanConnectView.MapPreviewPoint(double.MaxValue / 2, double.MaxValue / 2, double.MaxValue, double.MaxValue, 1, 1));
    }

    [Theory]
    [InlineData(double.NaN, 0, 100, 100, 100, 100)]
    [InlineData(0, double.PositiveInfinity, 100, 100, 100, 100)]
    [InlineData(0, 0, double.NaN, 100, 100, 100)]
    [InlineData(0, 0, 100, 0, 100, 100)]
    [InlineData(0, 0, -1, 100, 100, 100)]
    [InlineData(0, 0, 100, 100, 0, 100)]
    [InlineData(-1, 0, 100, 100, 100, 100)]
    [InlineData(0, 0, double.Epsilon, 100, 1280, 720)]
    public void InvalidPointerGeometryDoesNotProduceCommands(double x, double y, double width, double height, int pixelsWide, int pixelsHigh) =>
        Assert.Null(LanConnectView.MapPreviewPoint(x, y, width, height, pixelsWide, pixelsHigh));

    [Fact]
    public Task SeparateControlDenialKeepsViewingWithoutApplyingInput() => WpfHost.RunAsync(async () =>
    {
        await using var fixture = await Fixture.Connect();
        Assert.Equal(1, fixture.Selection.PrimarySelections);
        Assert.Equal(0, fixture.Selection.PickerSelections);
        var preview = Field<Image>(fixture.Viewer, "Preview");
        var unapproved = fixture.Key(Key.A, true); preview.RaiseEvent(unapproved);
        preview.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        Assert.False(unapproved.Handled);
        Assert.Empty(fixture.Sink.Commands);
        await fixture.DecideControl(false);
        Assert.NotNull(fixture.Viewer.PreviewSource);
        Assert.True(Field<Button>(fixture.Viewer, "RequestControlButton").IsEnabled);
        Assert.False(Field<Button>(fixture.Viewer, "ReleaseControlButton").IsEnabled);
        Assert.Empty(fixture.Sink.Commands);
        Assert.DoesNotContain(Application.Current.Windows.OfType<Window>(), window => window is SessionControlIndicator);
    });

    [Fact]
    public Task ApprovedKeyboardEventsReachRecordingSinkAndHostRevokeStopsFurtherInput() => WpfHost.RunAsync(async () =>
    {
        await using var fixture = await Fixture.Connect();
        await fixture.DecideControl(true);
        await fixture.FocusPreview();
        var preview = Field<Image>(fixture.Viewer, "Preview");
        var down = fixture.Key(Key.A, true); preview.RaiseEvent(down);
        var up = fixture.Key(Key.A, false); preview.RaiseEvent(up);
        await WaitFor(() => fixture.Sink.Commands.Count >= 2);
        Assert.True(down.Handled); Assert.True(up.Handled);
        Assert.Collection(fixture.Sink.Commands.ToArray(), command => Assert.Equal(new RemoteInputCommand(RemoteInputKind.KeyDown, Code: 65), command), command => Assert.Equal(new RemoteInputCommand(RemoteInputKind.KeyUp, Code: 65), command));
        var indicator = Application.Current.Windows.OfType<SessionControlIndicator>().Single();
        Assert.True(indicator.Topmost);
        var releases = fixture.Sink.Releases;
        Field<Button>(fixture.Host, "RevokeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => !Field<Button>(fixture.Viewer, "ReleaseControlButton").IsEnabled && fixture.Sink.Releases > releases);
        var afterRevoke = fixture.Key(Key.B, true); preview.RaiseEvent(afterRevoke);
        Assert.False(afterRevoke.Handled);
        Assert.Equal(2, fixture.Sink.Commands.Count);
        Assert.NotNull(fixture.Viewer.PreviewSource);
        Assert.DoesNotContain(Application.Current.Windows.OfType<Window>(), window => window is SessionControlIndicator);
        await fixture.Viewer.StopAsync();
        preview.RaiseEvent(fixture.Key(Key.C, true));
        Assert.Null(fixture.Viewer.PreviewSource);
        Assert.Equal(2, fixture.Sink.Commands.Count);
    });

    [Fact]
    public Task LeavingPreviewFocusReleasesHeldRemoteKeyWhileViewingContinues() => WpfHost.RunAsync(async () =>
    {
        await using var fixture = await Fixture.Connect();
        await fixture.DecideControl(true);
        await fixture.FocusPreview();
        Field<Image>(fixture.Viewer, "Preview").RaiseEvent(fixture.Key(Key.LeftCtrl, true));
        await WaitFor(() => fixture.Sink.Commands.Count == 1);
        var releases = fixture.Sink.Releases;
        Field<Button>(fixture.Viewer, "DisconnectButton").Focus();
        await WaitFor(() => fixture.Sink.Releases > releases && !Field<Button>(fixture.Viewer, "ReleaseControlButton").IsEnabled);
        Assert.Empty(fixture.Sink.HeldKeys);
        Assert.NotNull(fixture.Viewer.PreviewSource);
    });

    [Fact]
    public Task ApprovedMouseRoutedEventsUseLetterboxCoordinatesAndBoundedWheelDelta() => WpfHost.RunAsync(async () =>
    {
        await using var fixture = await Fixture.Connect();
        await fixture.DecideControl(true); await fixture.FocusPreview();
        var preview = Field<Image>(fixture.Viewer, "Preview");
        fixture.Viewer.PointerPosition = _ => new Point(preview.ActualWidth / 2, preview.ActualHeight / 2);
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent };
        var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent };
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 2400) { RoutedEvent = Mouse.MouseWheelEvent };
        preview.RaiseEvent(down); preview.RaiseEvent(up); preview.RaiseEvent(wheel);
        await WaitFor(() => fixture.Sink.Commands.Count >= 3);
        Assert.True(down.Handled); Assert.True(up.Handled); Assert.True(wheel.Handled);
        Assert.Collection(fixture.Sink.Commands.ToArray(),
            command => Assert.Equal(new RemoteInputCommand(RemoteInputKind.ButtonDown, 32768, 32768, 1), command),
            command => Assert.Equal(new RemoteInputCommand(RemoteInputKind.ButtonUp, 32768, 32768, 1), command),
            command => Assert.Equal(new RemoteInputCommand(RemoteInputKind.Wheel, 32768, 32768, Delta: 1200), command));
        Field<Button>(fixture.Viewer, "ReleaseControlButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => !Field<Button>(fixture.Viewer, "ReleaseControlButton").IsEnabled);
        preview.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        Assert.Equal(3, fixture.Sink.Commands.Count);
    });

    [Fact]
    public Task DeactivatingOwnedViewerWindowRevokesControlAndReleasesKeys() => WpfHost.RunAsync(async () =>
    {
        await using var fixture = await Fixture.Connect();
        await fixture.DecideControl(true); await fixture.FocusPreview();
        Field<Image>(fixture.Viewer, "Preview").RaiseEvent(fixture.Key(Key.A, true));
        await WaitFor(() => fixture.Sink.Commands.Count == 1);
        var releases = fixture.Sink.Releases;
        var otherOwnedWindow = new Window { Title = "DeskSteer — owned focus fixture", Width = 200, Height = 100 };
        try
        {
            otherOwnedWindow.Show(); otherOwnedWindow.Activate();
            await WaitFor(() => fixture.Sink.Releases > releases && !Field<Button>(fixture.Viewer, "ReleaseControlButton").IsEnabled);
            Assert.Empty(fixture.Sink.HeldKeys);
            Assert.NotNull(fixture.Viewer.PreviewSource);
        }
        finally { otherOwnedWindow.Close(); }
    });

    private sealed class Fixture : IAsyncDisposable
    {
        public RecordingSink Sink { get; } = new();
        public Selection Selection { get; }
        public LanHostView Host { get; }
        public LanConnectView Viewer { get; } = new(new Diagnostics());
        private Window Owner { get; }
        private Fixture()
        {
            Selection = new(Sink);
            Host = new(Selection, new Diagnostics(), (_, _) => Task.FromResult(true));
            Owner = new() { Title = "DeskSteer — owned input event fixture", Width = 820, Height = 700, Content = Viewer };
        }
        public static async Task<Fixture> Connect()
        {
            var fixture = new Fixture();
            try
            {
                fixture.Owner.Show();
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                Field<TextBox>(fixture.Host, "AddressInput").Text = "127.0.0.1";
                Field<TextBox>(fixture.Host, "PortInput").Text = port.ToString();
                Field<CheckBox>(fixture.Host, "ControlMode").IsChecked = true;
                await fixture.Host.StartAsync(fixture.Owner);
                Field<TextBox>(fixture.Viewer, "AddressInput").Text = "127.0.0.1";
                Field<TextBox>(fixture.Viewer, "PortInput").Text = port.ToString();
                Field<TextBox>(fixture.Viewer, "FingerprintInput").Text = Field<TextBox>(fixture.Host, "FingerprintText").Text;
                Field<PasswordBox>(fixture.Viewer, "PairingCodeInput").Password = Field<TextBox>(fixture.Host, "PairingCodeText").Text;
                Field<TextBox>(fixture.Viewer, "ViewerNameInput").Text = "owned test viewer";
                await fixture.Viewer.ConnectAsync();
                await WaitFor(() => fixture.Viewer.PreviewSource != null);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async Task DecideControl(bool approve)
        {
            Field<Button>(Viewer, "RequestControlButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => Application.Current.Windows.OfType<Window>().Any(window => window.Title == "Approve keyboard and mouse control"));
            var dialog = Application.Current.Windows.OfType<Window>().Single(window => window.Title == "Approve keyboard and mouse control");
            var body = (StackPanel)((ScrollViewer)dialog.Content).Content;
            body.Children.OfType<Button>().Single(button => Equals(button.Content, approve ? "Approve keyboard and mouse" : "Decline")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => approve ? Field<Button>(Viewer, "ReleaseControlButton").IsEnabled : Field<Button>(Viewer, "RequestControlButton").IsEnabled);
        }
        public async Task FocusPreview()
        {
            Owner.UpdateLayout(); Owner.Activate(); Field<Image>(Viewer, "Preview").Focus();
            await WaitFor(() => Field<Image>(Viewer, "Preview").IsKeyboardFocusWithin);
        }
        public KeyEventArgs Key(Key key, bool down) => new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Owner), Environment.TickCount, key)
        { RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent };
        public async ValueTask DisposeAsync() { await Viewer.StopAsync(); await Host.StopAsync(); Owner.Close(); }
    }
    private sealed class Selection(RecordingSink sink) : ICaptureSelection, IDesktopControlSelection
    {
        public bool IsSupported => true;
        public int PrimarySelections { get; private set; }
        public int PickerSelections { get; private set; }
        public Task<ControlledCapture> SelectPrimaryDisplayAsync(Window owner, CancellationToken token)
        { token.ThrowIfCancellationRequested(); PrimarySelections++; return Task.FromResult(new ControlledCapture(new Pixels(), sink)); }
        public Task<IScreenCapture?> SelectAsync(Window owner, CancellationToken token)
        { PickerSelections++; return Task.FromResult<IScreenCapture?>(new Pixels()); }
    }
    private sealed class Pixels : IScreenCapture
    {
        public string DisplayName => "owned test pixels";
        public CaptureMetrics Metrics => new(1, 0, 0, 1);
        public async IAsyncEnumerable<ScreenFrame> FramesAsync([EnumeratorCancellation] CancellationToken token = default)
        { yield return new(2, 1, [20, 40, 60, 255, 20, 40, 60, 255], TimeSpan.Zero); await Task.Delay(Timeout.Infinite, token); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class RecordingSink : IRemoteInputSink
    {
        public ConcurrentQueue<RemoteInputCommand> Commands { get; } = new();
        public ConcurrentDictionary<int, byte> HeldKeys { get; } = new();
        private int releases;
        public int Releases => Volatile.Read(ref releases);
        public void Apply(RemoteInputCommand command)
        { Commands.Enqueue(command); if (command.Kind == RemoteInputKind.KeyDown) HeldKeys.TryAdd(command.Code, 0); if (command.Kind == RemoteInputKind.KeyUp) HeldKeys.TryRemove(command.Code, out _); }
        public void ReleaseAll() { HeldKeys.Clear(); Interlocked.Increment(ref releases); }
    }
    private static T Field<T>(FrameworkElement view, string name) where T : class => (T)view.FindName(name);
    private static async Task WaitFor(Func<bool> condition)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); while (!condition()) await Task.Delay(20, timeout.Token); }
}
