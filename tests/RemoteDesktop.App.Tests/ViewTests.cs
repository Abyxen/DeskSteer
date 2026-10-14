using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteDesktop.Core;
using Xunit;
using Page = RemoteDesktop.Core.Page;

namespace RemoteDesktop.App.Tests;

public sealed class ViewTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ProjectRemoteViewTests", Guid.NewGuid().ToString("N"));
    private PreferencesStore Store => new(Path.Combine(directory, "preferences.json"));
    private MainWindow Create(IDiagnosticDestination? destination = null) => new(new NavigationState(), Store, new Diagnostics(), NullLogger<MainWindow>.Instance, destination ?? new TestDestination(null), new NoCaptureSelection());
    [Fact]
    public Task EveryNavigationButtonRendersItsDestination() => WpfHost.RunAsync(async () =>
    {
        var window = Create();
        try
        {
            Assert.False(((StackPanel)window.FindName("Navigation")).IsEnabled);
            await window.InitializeAsync();
            Assert.True(((StackPanel)window.FindName("Navigation")).IsEnabled);
            var headings = new[] { "A closer connection.", "Connect", "Host a session", "Devices", "Settings", "Made to connect." };
            var buttons = ((StackPanel)window.FindName("Navigation")).Children.OfType<Button>().ToArray();
            for (var index = 0; index < buttons.Length; index++)
            {
                buttons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(headings[index], ((TextBlock)window.FindName("Heading")).Text);
                Assert.NotEmpty(((StackPanel)window.FindName("PageContent")).Children.Cast<UIElement>());
            }
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task SaveButtonPersistsPreferenceAndNewWindowOpensOnSettings() => WpfHost.RunAsync(async () =>
    {
        var window = Create();
        try
        {
            await window.InitializeAsync();
            Navigate(window, Page.Settings);
            Descendants<CheckBox>(window).Single().IsChecked = false;
            Descendants<Button>(window).Single(b => Equals(b.Content, "Save preferences")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => ((TextBlock)window.FindName("Status")).Text == "Preferences saved.");
            Assert.False((await Store.LoadAsync()).OpenOnHome);
        }
        finally { window.Close(); }
        var next = Create();
        try { await next.InitializeAsync(); Assert.Equal("Settings", ((TextBlock)next.FindName("Heading")).Text); }
        finally { next.Close(); }
    });
    [Fact]
    public Task DiagnosticExportWritesBoundedAllowlistedEventsAndCancelWritesNothing() => WpfHost.RunAsync(async () =>
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "diagnostics.json");
        var diagnostics = new Diagnostics();
        for (var i = 0; i < 250; i++) diagnostics.Record(DiagnosticEvent.Navigated);
        var destination = new TestDestination(path);
        var window = new MainWindow(new(), Store, diagnostics, NullLogger<MainWindow>.Instance, destination, new NoCaptureSelection());
        try
        {
            await window.InitializeAsync(); Navigate(window, Page.About);
            var export = Descendants<Button>(window).Single(b => Equals(b.Content, "Export diagnostics…"));
            export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(200, json.RootElement.GetProperty("events").GetArrayLength());
            foreach (var entry in json.RootElement.GetProperty("events").EnumerateArray())
                Assert.Equal(new[] { "timestamp", "eventName" }, entry.EnumerateObject().Select(p => p.Name));
            destination.Path = null;
            export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task CorruptSettingsShowRecoveryMessage() => WpfHost.RunAsync(async () =>
    {
        Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "preferences.json"), "broken");
        var window = Create();
        try { await window.InitializeAsync(); Assert.Contains("could not be read", ((TextBlock)window.FindName("Status")).Text); }
        finally { window.Close(); }
    });
    [Fact]
    public Task RenderActualWpfViewsAtDefaultAndMinimumSizes() => WpfHost.RunAsync(async () =>
    {
        var window = Create();
        try
        {
            await window.InitializeAsync();
            var output = Environment.GetEnvironmentVariable("PROJECTREMOTE_EVIDENCE_DIR");
            foreach (var page in Enum.GetValues<Page>())
            foreach (var size in new[] { new Size(1140, 740), new Size(820, 580) })
            {
                Navigate(window, page);
                var root = (FrameworkElement)window.Content;
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                Assert.True(((TextBlock)window.FindName("Heading")).ActualWidth > 0);
                if (output != null)
                {
                    Directory.CreateDirectory(output);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"{page}-{size.Width}.png")); png.Save(stream);
                }
            }
        }
        finally { window.Close(); }
    });
    private static void Navigate(MainWindow window, Page page) => ((StackPanel)window.FindName("Navigation")).Children.OfType<Button>().Single(b => Equals(b.Tag, page.ToString())).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    private sealed class TestDestination(string? path) : IDiagnosticDestination
    {
        public string? Path { get; set; } = path;
        public string? ChoosePath(Window owner) => Path;
    }
    private sealed class NoCaptureSelection : ICaptureSelection
    {
        public bool IsSupported => false;
        public Task<IScreenCapture?> SelectAsync(Window owner, CancellationToken cancellationToken) => Task.FromResult<IScreenCapture?>(null);
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
