using System.Windows;
using System.Windows.Interop;
using DeskSteer.Capture;
using DeskSteer.Core;

namespace DeskSteer.App;

public interface ICaptureSelection
{
    bool IsSupported { get; }
    Task<IScreenCapture?> SelectAsync(Window owner, CancellationToken cancellationToken);
}

public sealed partial class CaptureSelection : ICaptureSelection
{
    private readonly CapturePicker picker = new();
    public bool IsSupported => picker.IsSupported;
    public Task<IScreenCapture?> SelectAsync(Window owner, CancellationToken cancellationToken) => picker.PickAsync(new WindowInteropHelper(owner).EnsureHandle(), cancellationToken);
}
