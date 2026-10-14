using System.Windows;
using System.Windows.Threading;

namespace RemoteDesktop.App.Tests;

// Runs only our own views on an STA dispatcher, without desktop input injection.
internal static class WpfHost
{
    private static readonly Task<Dispatcher> Ready = Start();
    private static Task<Dispatcher> Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var application = new App();
                application.InitializeComponent();
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception error) { ready.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }
    public static async Task RunAsync(Func<Task> action)
    {
        var dispatcher = await Ready.WaitAsync(TimeSpan.FromSeconds(10));
        await dispatcher.InvokeAsync(action).Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(20));
    }
}
