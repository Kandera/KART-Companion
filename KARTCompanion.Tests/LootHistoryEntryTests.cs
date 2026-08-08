using KARTCompanion.SavedVariables;

namespace KARTCompanion.Tests;

public class LootHistoryEntryTests
{
    // instance/instanceID are new fields the addon has started logging. Every award archived
    // before that change has neither key at all — not present-and-null — and that must stay true
    // of every award that already exists, forever (see this class's own remarks on absence).
    [Fact]
    public void Instance_FieldAbsent_ReturnsNull()
    {
        var entry = new LootHistoryEntry(new Dictionary<string, object?> { ["time"] = 1d });

        Assert.Null(entry.Instance);
        Assert.Null(entry.InstanceId);
    }

    [Fact]
    public void Instance_FieldPresent_ReturnsTheStoredValues()
    {
        var entry = new LootHistoryEntry(new Dictionary<string, object?>
        {
            ["time"] = 1d,
            ["instance"] = "March on Quel'Danas",
            ["instanceID"] = 2652d,
        });

        Assert.Equal("March on Quel'Danas", entry.Instance);
        Assert.Equal(2652, entry.InstanceId);
    }
}
