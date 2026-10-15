using DeskSteer.Core;
using Xunit;

namespace DeskSteer.Core.Tests;

public sealed class FoundationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DeskSteerTests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(directory, "settings.json");
    [Fact] public void NavigationRejectsUnknownPages()
    {
        var navigation = new NavigationState();
        Assert.Throws<ArgumentOutOfRangeException>(() => navigation.Navigate((Page)999));
        Assert.Equal(Page.Home, navigation.Current);
    }
    [Fact] public void NavigationNotifiesOnlyOnChange()
    {
        var navigation = new NavigationState(); var notifications = 0;
        navigation.PropertyChanged += (_, _) => notifications++;
        foreach (var page in Enum.GetValues<Page>()) navigation.Navigate(page);
        navigation.Navigate(Page.About);
        Assert.Equal(5, notifications);
    }
    [Fact] public async Task MissingSettingsUseDefaults() => Assert.True((await new PreferencesStore(SettingsPath).LoadAsync()).OpenOnHome);
    [Fact] public async Task SettingsSurviveNewStoreInstance()
    {
        await new PreferencesStore(SettingsPath).SaveAsync(new(false));
        Assert.False((await new PreferencesStore(SettingsPath).LoadAsync()).OpenOnHome);
        Assert.Single(Directory.GetFiles(directory));
    }
    [Fact] public async Task CorruptSettingsAreNotSilentlyOverwritten()
    {
        Directory.CreateDirectory(directory); await File.WriteAllTextAsync(SettingsPath, "invalid");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new PreferencesStore(SettingsPath).LoadAsync());
        Assert.Equal("invalid", await File.ReadAllTextAsync(SettingsPath));
    }
    [Fact] public async Task OversizedSettingsAreRejected()
    {
        Directory.CreateDirectory(directory); await File.WriteAllTextAsync(SettingsPath, new string(' ', 4097));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PreferencesStore(SettingsPath).LoadAsync());
    }
    [Fact] public async Task CancelledSavePreservesExistingFileAndCleansTemporaryFile()
    {
        var store = new PreferencesStore(SettingsPath); await store.SaveAsync(new(false));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(true), cancellation.Token));
        Assert.False((await store.LoadAsync()).OpenOnHome); Assert.Single(Directory.GetFiles(directory));
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
