using KARTCompanion.Export;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Tests;

public class RcLootCouncilJsonWriterTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // Two implementations written independently from the same specification, over the same input,
    // must agree. The expectation was produced by the ADDON's own LH.BuildRCLootCouncilJSON run
    // against this very fixture — not by this code, and not by hand.
    //
    // What it proves and what it does not: the addon's test harness has no item database either, so
    // both sides emit empty subType and equipLoc. In the running game the addon fills those from
    // C_Item.GetItemInfoInstant and the Companion still cannot. So this pins field order, escaping,
    // date formatting and id derivation — not that the Companion matches the addon in-game.
    [Fact]
    public void Write_RealFixture_MatchesTheAddonsOwnOutput()
    {
        var entries = LootHistoryReader.Read(File.ReadAllText(FixturePath("loot-history-real.lua")));
        var expected = File.ReadAllText(FixturePath("loot-history-real.addon.json")).Trim();

        var actual = RcLootCouncilJsonWriter.Write(entries);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Write_Empty_ProducesAnEmptyJsonArray()
    {
        Assert.Equal("[]", RcLootCouncilJsonWriter.Write(Array.Empty<LootHistoryEntry>()));
    }

    // The id is positional within the exported list, exactly as the addon builds it. Two awards with
    // the same timestamp must therefore still get different ids.
    [Fact]
    public void Write_TwoAwardsAtTheSameSecond_GetDistinctIds()
    {
        var a = new LootHistoryEntry(new Dictionary<string, object?> { ["time"] = 100d, ["winner"] = "A" });
        var b = new LootHistoryEntry(new Dictionary<string, object?> { ["time"] = 100d, ["winner"] = "B" });

        var json = RcLootCouncilJsonWriter.Write(new[] { a, b });

        Assert.Contains("\"id\":\"100-1\"", json);
        Assert.Contains("\"id\":\"100-2\"", json);
    }

    // Free text reaches this output. A quote or a newline that is not escaped produces JSON the
    // other end cannot parse, and the failure surfaces at WoWUtils, not here.
    [Fact]
    public void Write_ReasonWithQuotesAndNewline_IsEscaped()
    {
        var entry = new LootHistoryEntry(new Dictionary<string, object?>
        {
            ["time"] = 1d,
            ["reason"] = "he said \"no\"\nthen left",
        });

        var json = RcLootCouncilJsonWriter.Write(new[] { entry });

        Assert.Contains("\\\"no\\\"", json);
        Assert.Contains("\\n", json);
        Assert.DoesNotContain("\n\"then left", json);
    }
}
