using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DeskSteer.Core;

namespace DeskSteer.App;

public partial class App : Application
{
    private ServiceProvider? services;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataPath = Environment.GetEnvironmentVariable("DESKSTEER_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskSteer");
        services = new ServiceCollection()
            .AddSingleton(new PreferencesStore(Path.Combine(dataPath, "preferences.json")))
            .AddSingleton<NavigationState>()
            .AddSingleton<Diagnostics>()
            .AddSingleton<IDiagnosticDestination, DiagnosticDestination>()
            .AddSingleton<ICaptureSelection, CaptureSelection>()
            .AddSingleton<ILoggerProvider, DiagnosticLoggerProvider>()
            .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information))
            .AddSingleton<MainWindow>().BuildServiceProvider();
        services.GetRequiredService<MainWindow>().Show();
    }
    protected override void OnExit(ExitEventArgs e) { services?.Dispose(); base.OnExit(e); }
}
