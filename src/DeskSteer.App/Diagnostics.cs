using System.Collections.Concurrent;
using System.Text.Json;

namespace DeskSteer.App;

// Only fixed event names are accepted; never accept arbitrary exception text or user data.
public enum DiagnosticEvent { Started, Navigated, SettingsLoaded, SettingsSaved, SettingsFailed, ExportFailed, CaptureStarted, CaptureStopped, CaptureFailed, LanHostStarted, LanHostStopped, LanViewerConnected, LanViewerStopped, LanSessionFailed, LanRequestApproved, LanRequestDeclined }
public sealed class Diagnostics
{
    private readonly ConcurrentQueue<object> events = new();
    public void Record(DiagnosticEvent name)
    {
        events.Enqueue(new { timestamp = DateTimeOffset.UtcNow, eventName = name.ToString() });
        while (events.Count > 200) events.TryDequeue(out _);
    }
    public string Export() => JsonSerializer.Serialize(new { product = "DeskSteer", version = "0.3.1", framework = Environment.Version.ToString(), events = events.ToArray() }, new JsonSerializerOptions { WriteIndented = true });
}
