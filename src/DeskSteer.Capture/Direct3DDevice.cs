using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;

namespace DeskSteer.Capture;

internal static class Direct3DDevice
{
    internal static IDirect3DDevice Create()
    {
        nint device = 0, context = 0, dxgi = 0, inspectable = 0;
        try
        {
            // BGRA support; the device is deliberately not SINGLETHREADED.
            var result = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out device, out _, out context);
            if (result < 0)
            {
                // Do not overwrite partial COM outputs from a failed hardware attempt.
                if (context != 0) Marshal.Release(context);
                if (device != 0) Marshal.Release(device);
                context = device = 0;
                result = D3D11CreateDevice(0, 5, 0, 0x20, 0, 0, 7, out device, out _, out context); // WARP fallback
            }
            Marshal.ThrowExceptionForHR(result);
            var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C"); // IDXGIDevice
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0) Marshal.Release(inspectable);
            if (dxgi != 0) Marshal.Release(dxgi);
            if (context != 0) Marshal.Release(context);
            if (device != 0) Marshal.Release(device);
        }
    }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint count, uint sdkVersion, out nint device, out uint featureLevel, out nint context);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
