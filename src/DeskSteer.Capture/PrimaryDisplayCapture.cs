using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskSteer.Core;
using Windows.Graphics.Capture;

namespace DeskSteer.Capture;

// This explicit primary-display path is used only after the host selects that sharing mode.
// Arbitrary window/display viewing continues to use the Windows capture picker.
public static class PrimaryDisplayCapture
{
    public static IScreenCapture Create()
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows Graphics Capture is unavailable.");
        var monitor = MonitorFromPoint(default, 1); // Primary monitor fallback, desktop origin.
        if (monitor == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "The primary display is unavailable.");
        var bridge = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var pointer = bridge.CreateForMonitor(monitor, in iid);
        try { return new WindowsScreenCapture(GraphicsCaptureItem.FromAbi(pointer)); }
        finally { Marshal.Release(pointer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint MonitorFromPoint(Point point, uint flags);
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }
}
