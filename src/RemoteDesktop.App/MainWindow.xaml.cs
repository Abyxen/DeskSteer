using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using RemoteDesktop.Core;
using Page = RemoteDesktop.Core.Page;

namespace RemoteDesktop.App;

public partial class MainWindow : Window
{
    private readonly NavigationState navigation;
    private readonly PreferencesStore store;
    private readonly Diagnostics diagnostics;
    private readonly ILogger<MainWindow> logger;
    private readonly IDiagnosticDestination destination;
    private readonly CaptureView captureView;
    private bool allowClose;
    private readonly CancellationTokenSource lifetime = new();
    private Preferences preferences = new();
    private bool saving;

    public MainWindow(NavigationState navigation, PreferencesStore store, Diagnostics diagnostics, ILogger<MainWindow> logger, IDiagnosticDestination destination, ICaptureSelection? captureSelection = null)
    {
        InitializeComponent();
        this.navigation = navigation; this.store = store; this.diagnostics = diagnostics; this.logger = logger;
        this.destination = destination;
        captureView = new CaptureView(captureSelection ?? new CaptureSelection(), diagnostics);
        navigation.PropertyChanged += NavigationChanged;
        Loaded += async (_, _) => await InitializeAsync();
        Closing += async (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true;
            await captureView.StopAsync();
            // Closing cannot be called recursively inside its own event, even when Stop completes synchronously.
            await Task.Yield();
            allowClose = true;
            Close();
        };
        Closed += (_, _) => { navigation.PropertyChanged -= NavigationChanged; lifetime.Cancel(); lifetime.Dispose(); };
        Render();
        Navigation.IsEnabled = false;
        PageContent.IsEnabled = false;
        Status.Text = "Loading local preferences…";
    }
    private void NavigationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Render();
    internal async Task InitializeAsync()
    {
        diagnostics.Record(DiagnosticEvent.Started);
        try
        {
            preferences = await store.LoadAsync(lifetime.Token);
            diagnostics.Record(DiagnosticEvent.SettingsLoaded);
            if (!preferences.OpenOnHome) navigation.Navigate(Page.Settings);
            else if (navigation.Current == Page.Settings) Render();
            else Status.Text = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { Status.Text = "Settings could not be read. Defaults are in use; saving replaces the settings file."; diagnostics.Record(DiagnosticEvent.SettingsFailed); }
        finally { Navigation.IsEnabled = true; PageContent.IsEnabled = true; }
    }
    private async void NavigateClick(object sender, RoutedEventArgs e)
    {
        var page = Enum.Parse<Page>((string)((Button)sender).Tag);
        if (navigation.Current == Page.Host && page != Page.Host) await captureView.StopAsync();
        navigation.Navigate(page);
    }
    private void Render()
    {
        PageContent.Children.Clear(); Status.Text = "";
        foreach (Button button in Navigation.Children) button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString((string)button.Tag == navigation.Current.ToString() ? "#36594F" : "#293444"));
        logger.LogInformation(new EventId((int)DiagnosticEvent.Navigated), "Navigation changed to {Page}", navigation.Current);
        switch (navigation.Current)
        {
            case Page.Home:
                Heading.Text = "A closer connection."; Subtitle.Text = "A considered workspace for remote support. Built around your consent.";
                AddCard("A real preview, on your terms", "Select a display or window for a live local preview. Remote connections are a later milestone.");
                AddCard("01   Connect to a computer", "The next connection milestone will bring authenticated, encrypted streaming to your local network.", "Explore connection status", () => navigation.Navigate(Page.Connect));
                AddCard("02   Preview your workspace", "Choose a display or window using the Windows picker. Stop capture at any time.", "Open local preview", () => navigation.Navigate(Page.Host));
                break;
            case Page.Connect:
                Heading.Text = "Connect"; Subtitle.Text = "Local network first. Authenticated connections by design.";
                AddCard("Connections are unavailable", "LAN streaming is scheduled for Milestone 3, after real screen capture is verified. This build opens no listening ports and makes no remote connections."); break;
            case Page.Host:
                Heading.Text = "Host a session"; Subtitle.Text = "Your screen stays yours.";
                PageContent.Children.Add(captureView); break;
            case Page.Devices:
                Heading.Text = "Devices"; Subtitle.Text = "A home for the computers you trust.";
                AddCard("No saved devices", "Device pairing and saved-device management are planned for later milestones. No devices have been registered, discovered or contacted."); break;
            case Page.Settings:
                Heading.Text = "Settings"; Subtitle.Text = "Small preferences. Clear behaviour.";
                var panel = AddCard("On application launch", "Choose which page appears when you open Project Remote. This does not start the application with Windows.");
                var checkbox = new CheckBox { Content = "Open on Home (otherwise open Settings)", IsChecked = preferences.OpenOnHome, Foreground = Brushes.White, Margin = new Thickness(0,18,0,18) };
                panel.Children.Add(checkbox);
                var save = new Button { Content = "Save preferences", HorizontalAlignment = HorizontalAlignment.Left };
                save.Click += async (_, _) =>
                {
                    if (saving) return;
                    saving = true; save.IsEnabled = false;
                    try { var next = new Preferences(checkbox.IsChecked == true); await store.SaveAsync(next, lifetime.Token); preferences = next; diagnostics.Record(DiagnosticEvent.SettingsSaved); Status.Text = "Preferences saved."; }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { diagnostics.Record(DiagnosticEvent.SettingsFailed); Status.Text = "Could not save preferences. Check local storage permissions and free space."; }
                    finally { saving = false; save.IsEnabled = true; }
                };
                panel.Children.Add(save); break;
            case Page.About:
                Heading.Text = "Made to connect."; Subtitle.Text = "Project Remote · 0.2.0 · Local capture preview";
                AddCard("Built for Windows", $"WPF · .NET {Environment.Version}\nThis is an incremental development build, not a production remote-access product.");
                AddCard("Diagnostics you can inspect", "Export a bounded set of event names and timestamps from this run. Exports exclude machine names, file paths, credentials, keystrokes and screen content.", "Export diagnostics…", ExportDiagnostics);
                break;
        }
    }
    private StackPanel AddCard(string title, string description, string? action = null, Action? click = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = description, Foreground = new SolidColorBrush(Color.FromRgb(166,180,199)), Margin = new Thickness(0,12,0,0), LineHeight = 24 });
        if (action != null) { var button = new Button { Content = action, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,22,0,0) }; button.Click += (_, _) => click?.Invoke(); panel.Children.Add(button); }
        PageContent.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = panel });
        return panel;
    }
    private void ExportDiagnostics()
    {
        var path = destination.ChoosePath(this);
        if (path == null) return;
        try { File.WriteAllText(path, diagnostics.Export()); Status.Text = "Diagnostics exported."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { diagnostics.Record(DiagnosticEvent.ExportFailed); Status.Text = "Export failed. Choose a writable folder."; }
    }
}
