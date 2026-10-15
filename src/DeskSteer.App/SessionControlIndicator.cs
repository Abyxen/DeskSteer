using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeskSteer.App;

/// <summary>Remains visible when the main window is minimized; closing it revokes control.</summary>
internal sealed class SessionControlIndicator : Window
{
    private const int HotkeyId = 0x4453;
    private readonly Func<Task> revoke;
    private HwndSource? source;
    private bool registered;
    private bool dismissed;

    internal SessionControlIndicator(Func<Task> revoke, Func<Task> stop)
    {
        this.revoke = revoke;
        Title = "DeskSteer — remote control active";
        Width = 460; SizeToContent = SizeToContent.Height;
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow; Background = new SolidColorBrush(Color.FromRgb(29, 38, 50));
        Left = SystemParameters.WorkArea.Right - Width - 16; Top = SystemParameters.WorkArea.Top + 16;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "REMOTE KEYBOARD AND MOUSE ACTIVE", FontWeight = FontWeights.Bold, Foreground = Brushes.Aquamarine });
        var shortcut = new TextBlock { Text = "Use the controls below to stop remote input.", Margin = new Thickness(0, 8, 0, 12) };
        panel.Children.Add(shortcut);
        var revokeButton = new Button { Content = "Revoke control — keep viewing" };
        var stopButton = new Button { Content = "Stop sharing and disconnect" };
        revokeButton.Click += async (_, _) => await revoke();
        stopButton.Click += async (_, _) => await stop();
        panel.Children.Add(revokeButton); panel.Children.Add(stopButton); Content = panel;
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(HandleMessage);
            registered = source != null && RegisterHotKey(source.Handle, HotkeyId, 0x4000 | 0x0001 | 0x0002, 0x7B);
            if (registered) shortcut.Text = "Ctrl + Alt + F12 revokes control immediately.";
        };
        Closed += async (_, _) =>
        {
            if (registered && source != null) UnregisterHotKey(source.Handle, HotkeyId);
            source?.RemoveHook(HandleMessage);
            if (!dismissed) await revoke();
        };
    }

    internal void Dismiss() { dismissed = true; Close(); }
    private nint HandleMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == HotkeyId)
        {
            handled = true;
            _ = Dispatcher.InvokeAsync(async () => await revoke());
        }
        return 0;
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
