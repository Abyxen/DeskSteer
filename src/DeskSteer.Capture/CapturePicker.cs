using DeskSteer.Core;
using Windows.Graphics.Capture;

namespace DeskSteer.Capture;

public sealed class CapturePicker
{
    public bool IsSupported => GraphicsCaptureSession.IsSupported();
    public async Task<IScreenCapture?> PickAsync(nint ownerWindow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupported) throw new NotSupportedException("Windows Graphics Capture is not available on this system.");
        var picker = new GraphicsCapturePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindow);
        var item = await picker.PickSingleItemAsync().AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return item == null ? null : new WindowsScreenCapture(item);
    }
}
