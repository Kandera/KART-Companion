using KARTCompanion.Matching;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Tests;

public class LuaTableWriterTests
{
    [Fact]
    public void WriteAssignment_ProducesExpectedShapeAndOnlyOneColumnZeroClosingBrace()
    {
        var syncedAt = DateTimeOffset.FromUnixTimeSeconds(1752300000);
        var model = new CacheModel
        {
            SyncedAt = syncedAt,
            GroupId = "abc123",
            Players = new Dictionary<string, List<GainCandidate>>
            {
                ["kandera-blackmoore"] = new()
                {
                    new GainCandidate(249339, 279, "trinket1", 3.42, "raidbots", "mythic-max", syncedAt),
                    new GainCandidate(249808, 298, null, 1.29, "qelive", "heroic-max", syncedAt),
                },
            },
        };

        var text = LuaTableWriter.WriteAssignment(model);

        Assert.StartsWith("KART_WoWUtilsCache = {\n", text);
        Assert.Contains("[\"schemaVersion\"] = 1,", text);
        Assert.Contains("[\"syncedAt\"] = 1752300000,", text);
        Assert.Contains("[\"groupId\"] = \"abc123\",", text);
        Assert.Contains("[\"kandera-blackmoore\"] = {", text);
        Assert.Contains("[\"itemId\"] = 249339,", text);
        Assert.Contains("[\"gainPct\"] = 3.42,", text);
        Assert.Contains("[\"source\"] = \"raidbots\",", text);

        // Exactly one line must be a bare "}" (no indent, no trailing comma) — the block-writer
        // depends on this to isolate the whole assignment. Every nested table's own closing
        // brace must therefore be indented and/or comma-suffixed.
        var bareCloseCount = text.Split('\n').Count(l => l == "}");
        Assert.Equal(1, bareCloseCount);
    }

    [Fact]
    public void WriteAssignment_OutputIsValidLuaBraceBalance()
    {
        var model = new CacheModel
        {
            SyncedAt = DateTimeOffset.UtcNow,
            Players = new Dictionary<string, List<GainCandidate>>
            {
                ["a-realm"] = new() { new GainCandidate(1, 1, "head", 0.5, "raidbots", null, DateTimeOffset.UtcNow) },
                ["b-realm"] = new(),
            },
        };

        var text = LuaTableWriter.WriteAssignment(model);

        var opens = text.Count(c => c == '{');
        var closes = text.Count(c => c == '}');
        Assert.Equal(opens, closes);
    }
}
