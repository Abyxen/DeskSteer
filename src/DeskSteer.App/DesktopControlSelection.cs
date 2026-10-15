using System.Windows;
using DeskSteer.Capture;
using DeskSteer.Core;
using DeskSteer.Input;

namespace DeskSteer.App;

public sealed record ControlledCapture(IScreenCapture Capture, IRemoteInputSink Input);

public interface IDesktopControlSelection
{
    Task<ControlledCapture> SelectPrimaryDisplayAsync(Window owner, CancellationToken token);
}

public sealed partial class CaptureSelection : IDesktopControlSelection
{
    public Task<ControlledCapture> SelectPrimaryDisplayAsync(Window owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var sink = WindowsInputSink.ForPrimaryDisplay();
        try { return Task.FromResult(new ControlledCapture(PrimaryDisplayCapture.Create(), sink)); }
        catch { sink.ReleaseAll(); throw; }
    }
}
