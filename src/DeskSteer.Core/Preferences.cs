using System.Text.Json;

namespace DeskSteer.Core;

public sealed record Preferences(bool OpenOnHome = true);

public sealed class PreferencesStore(string path)
{
    public async Task<Preferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Settings file exceeds its size limit.");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Preferences>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Settings file is empty.");
    }

    public async Task SaveAsync(Preferences preferences, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(preferences), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
