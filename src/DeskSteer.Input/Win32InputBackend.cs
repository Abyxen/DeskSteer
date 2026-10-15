using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeskSteer.Input;

internal sealed class Win32InputBackend : IInputBackend
{
    public int LastError { get; private set; }
    public bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    public uint Send(NativeInput input)
    {
        var sent = SendInput(1, [input], Marshal.SizeOf<NativeInput>());
        LastError = Marshal.GetLastWin32Error(); return sent;
    }
    public DisplayGeometry GetGeometry()
    {
        // WPF's logical coordinates must never enter the physical-pixel mapping.
        var previous = SetThreadDpiAwarenessContext(new nint(-4)); // PER_MONITOR_AWARE_V2
        if (previous == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read DPI-aware display geometry.");
        try
        {
            MonitorInfo? primary = null;
            Exception? failure = null;
            List<string> displays = [];
            bool Visit(nint monitor, nint dc, ref NativeRect bounds, nint data)
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info)) { failure = new Win32Exception(Marshal.GetLastWin32Error()); return false; }
                if ((info.Flags & 1) != 0) primary = info;
                displays.Add($"{info.Device}:{info.Monitor.Left},{info.Monitor.Top},{info.Monitor.Right},{info.Monitor.Bottom}");
                return true;
            }
            if (!EnumDisplayMonitors(0, 0, Visit, 0)) throw failure ?? new Win32Exception(Marshal.GetLastWin32Error());
            if (primary is not { } main) throw new InvalidOperationException("The primary display is unavailable.");
            displays.Sort(StringComparer.Ordinal);
            return new(main.Monitor.Left, main.Monitor.Top, main.Monitor.Right - main.Monitor.Left, main.Monitor.Bottom - main.Monitor.Top,
                GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79), string.Join(";", displays));
        }
        finally { _ = SetThreadDpiAwarenessContext(previous); }
    }
    private delegate bool MonitorVisitor(nint monitor, nint dc, ref NativeRect bounds, nint data);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? Device;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorVisitor visitor, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
