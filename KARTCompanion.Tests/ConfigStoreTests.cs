using System.Text.Json;
using KARTCompanion.Config;

namespace KARTCompanion.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "kart-companion-configstore-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task Save_CalledConcurrently_NeverThrowsAndLeavesAValidConfig()
    {
        // Simulates the background sync timer and the Settings dialog's Force Sync button both
        // calling ConfigStore.Save around the same time — must not corrupt config.json or throw.
        var tasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(() => ConfigStore.Save(new CompanionConfig { GroupKey = $"key-{i}" }, _configPath)))
            .ToArray();

        await Task.WhenAll(tasks);

        var json = await File.ReadAllTextAsync(_configPath);
        var saved = JsonSerializer.Deserialize<CompanionConfig>(json);
        Assert.NotNull(saved);
        Assert.StartsWith("key-", saved!.GroupKey);
    }

    [Fact]
    public void Save_DoesNotLeaveTempFilesBehind()
    {
        ConfigStore.Save(new CompanionConfig { GroupKey = "key" }, _configPath);

        var leftovers = Directory.GetFiles(_tempDir).Where(f => f != _configPath);
        Assert.Empty(leftovers);
    }

    // Final review, MINOR 1: the Settings dialog rebuilt CompanionConfig out of six named
    // properties, so LootHistoryReadAt was silently dropped on every OK — and so would be the next
    // field anyone adds. It now copies instead. Comparing the serialized form rather than a
    // hand-written list of properties is the point: a field added later is covered by this test
    // without anyone remembering to come back here, as long as the fixture below sets it.
    [Fact]
    public void Copy_CarriesEveryField_NotJustTheOnesSettingsKnowsAbout()
    {
        var original = new CompanionConfig
        {
            GroupKey = "key",
            WowInstallPath = @"C:\wow",
            SavedVariablesFilePath = @"C:\wow\sv.lua",
            SyncIntervalMinutes = 42,
            AutoSyncEnabled = false,
            LastSyncUtc = DateTimeOffset.FromUnixTimeSeconds(1785400000),
            LootHistoryReadAt = { [@"C:\wow\sv.lua"] = DateTimeOffset.FromUnixTimeSeconds(1785300000) },
            LootHistoryUnreadableNotifiedAt = { [@"C:\wow\sv.lua"] = DateTimeOffset.FromUnixTimeSeconds(1785200000) },
        };

        var copy = original.Copy();

        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(copy));
        // ...and the fixture really does differ from a default config in every writable field, so
        // "carried" above cannot be satisfied by two objects that are both empty.
        var fresh = new CompanionConfig();
        foreach (var property in typeof(CompanionConfig).GetProperties().Where(p => p.CanWrite))
        {
            Assert.NotEqual(
                JsonSerializer.Serialize(property.GetValue(fresh)),
                JsonSerializer.Serialize(property.GetValue(original)));
        }
    }

    // A shallow copy would share the one mutable collection, so changing the dialog's copy would
    // reach back into the config the tray is still using.
    [Fact]
    public void Copy_DoesNotShareTheReadStampDictionary()
    {
        var original = new CompanionConfig
        {
            LootHistoryReadAt = { [@"C:\wow\sv.lua"] = DateTimeOffset.FromUnixTimeSeconds(1785300000) },
        };

        var copy = original.Copy();
        copy.LootHistoryReadAt[@"D:\other.lua"] = DateTimeOffset.FromUnixTimeSeconds(1785310000);

        Assert.Single(original.LootHistoryReadAt);
    }
}
