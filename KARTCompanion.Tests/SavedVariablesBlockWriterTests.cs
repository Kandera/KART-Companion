using KARTCompanion.SavedVariables;

namespace KARTCompanion.Tests;

public class SavedVariablesBlockWriterTests
{
    // Mirrors the real KeineAhnungRaidTools.lua shape (KART_Settings has a nested table value,
    // e.g. lcCouncilPanelPos), confirmed against a real live SavedVariables file this session.
    private const string RealisticFileContent =
        "\nKART_Settings = {\n" +
        "[\"wuModuleEnabled\"] = true,\n" +
        "[\"lcCouncilPanelPos\"] = {\n" +
        "[\"y\"] = 1060,\n" +
        "[\"x\"] = 1576.999,\n" +
        "},\n" +
        "[\"lcAutoPass\"] = true,\n" +
        "}\n" +
        "KART_LootHistory = {\n" +
        "}\n" +
        "KART_LCOfficerNotes = {\n" +
        "[\"Kanderadk\"] = \"testertest\",\n" +
        "}\n";

    [Fact]
    public void ReplaceOrAppendBlock_VariableNotPresent_AppendsAtEnd()
    {
        var block = "KART_WoWUtilsCache = {\n    [\"schemaVersion\"] = 1,\n}";

        var result = SavedVariablesBlockWriter.ReplaceOrAppendBlock(RealisticFileContent, block);

        Assert.Contains(block, result);
        // Every original variable's content must survive byte-for-byte.
        Assert.Contains("[\"wuModuleEnabled\"] = true,", result);
        Assert.Contains("[\"lcCouncilPanelPos\"] = {", result);
        Assert.Contains("[\"Kanderadk\"] = \"testertest\",", result);
    }

    [Fact]
    public void ReplaceOrAppendBlock_VariableAlreadyPresent_ReplacesOnlyThatBlock_LeavesRestUntouched()
    {
        var existingBlock = "KART_WoWUtilsCache = {\n[\"schemaVersion\"] = 1,\n[\"players\"] = {\n[\"old-realm\"] = {\n},\n},\n}\n";
        var contentWithCache = RealisticFileContent + existingBlock;

        var newBlock = "KART_WoWUtilsCache = {\n    [\"schemaVersion\"] = 1,\n    [\"players\"] = {\n        [\"new-realm\"] = {\n        },\n    },\n}";

        var result = SavedVariablesBlockWriter.ReplaceOrAppendBlock(contentWithCache, newBlock);

        Assert.Contains(newBlock, result);
        Assert.DoesNotContain("old-realm", result);

        // The other three real variables are completely untouched — the highest-risk property
        // to verify, since a bug here would corrupt the user's real settings/loot history.
        Assert.Contains("[\"wuModuleEnabled\"] = true,", result);
        Assert.Contains("[\"lcCouncilPanelPos\"] = {\n[\"y\"] = 1060,\n[\"x\"] = 1576.999,\n},", result);
        Assert.Contains("[\"lcAutoPass\"] = true,", result);
        Assert.Contains("KART_LootHistory = {\n}", result);
        Assert.Contains("[\"Kanderadk\"] = \"testertest\",", result);

        // KART_WoWUtilsCache must appear exactly once (the old block, not just its contents,
        // must be gone — not merely shadowed/duplicated).
        var occurrences = System.Text.RegularExpressions.Regex.Matches(result, "KART_WoWUtilsCache = \\{").Count;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void ReplaceOrAppendBlock_EmptyOriginalContent_ProducesJustTheBlock()
    {
        var block = "KART_WoWUtilsCache = {\n    [\"schemaVersion\"] = 1,\n}";

        var result = SavedVariablesBlockWriter.ReplaceOrAppendBlock("", block);

        Assert.Equal(block.TrimEnd('\n') + "\n", result);
    }

    [Fact]
    public void ReplaceOrAppendBlock_RoundTrip_IsIdempotent()
    {
        var block = "KART_WoWUtilsCache = {\n    [\"schemaVersion\"] = 1,\n}";

        var once = SavedVariablesBlockWriter.ReplaceOrAppendBlock(RealisticFileContent, block);
        var twice = SavedVariablesBlockWriter.ReplaceOrAppendBlock(once, block);

        Assert.Equal(once, twice);
    }
}
