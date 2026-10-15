using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.Graphics.Capture;

namespace DeskSteer.App.Tests;

// Test-only target creation. It accepts a Window owned by this test process, not arbitrary HWNDs.
internal static class OwnedCaptureItem
{
    internal static GraphicsCaptureItem Create(Window fixture)
    {
        var handle = new WindowInteropHelper(fixture).Handle;
        if (handle == 0) throw new InvalidOperationException("Show the owned test window first.");
        var bridge = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemInterface = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var pointer = bridge.CreateForWindow(handle, in itemInterface);
        try { return GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint handle, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }
}
