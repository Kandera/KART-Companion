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
}
