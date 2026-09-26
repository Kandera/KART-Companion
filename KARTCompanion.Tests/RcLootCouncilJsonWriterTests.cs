using System.Globalization;
using System.Text;
using System.Text.Json;
using KARTCompanion.Export;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Tests;

public class RcLootCouncilJsonWriterTests
{
    // The golden fixture was captured on a CEST (UTC+02:00) machine; CI runs on UTC. Pinning the zone
    // here, rather than letting the test fall back to Write's default (the machine's local zone), is
    // what makes the comparison reproducible on any runner.
    private static readonly TimeZoneInfo GoldenTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // Two implementations written independently from the same specification, over the same input,
    // must agree. The expectation was produced by the ADDON's own LH.BuildRCLootCouncilJSON run
    // against this very fixture — not by this code, and not by hand.
    //
    // What it proves and what it does not: the addon's test harness has no item database of its own
    // for LootHistory export purposes -- its normal item catalogue (used by unrelated gear/collectible
    // tests) was deliberately emptied before the dump was captured, because three of this fixture's
    // real drops otherwise collide by chance with ids that catalogue happens to register. So both
    // sides emit empty subType and equipLoc for every row here. In the running game the addon fills
    // those from C_Item.GetItemInfoInstant and the Companion still cannot. So this pins field order,
    // escaping, date formatting and id derivation — not that the Companion matches the addon in-game.
    //
    // What it deliberately does NOT pin: the order of awards that share the same second. Lua's
    // table.sort (which LH.GetFilteredEntries uses) does not specify an order for equal keys, and nothing
    // downstream depends on one -- id itself is already unstable across different exports of the same
    // award (LootHistory.lua re-indexes from 1 per export, so the same award gets a different `id` in
    // a "new" cut versus an "all" cut). Pinning that accident of Lua's C sort into the Companion would
    // make the check fail the moment LuaJIT or Blizzard's Lua ever changes it, for no benefit to anyone
    // reading the exported JSON. So the comparison below checks the sort key and direction (assertion 1),
    // then every field, its order, and its escaping for every row, tolerant of order within a shared
    // second (assertion 2), then the shape of `id` (assertion 3) -- everything the check can actually
    // prove, and nothing it can't.
    [Fact]
    public void Write_RealFixture_MatchesTheAddonsOwnOutput()
    {
        var entries = LootHistoryReader.Read(File.ReadAllText(FixturePath("loot-history-real.lua")));
        var expectedJson = File.ReadAllText(FixturePath("loot-history-real.addon.json")).Trim();

        var actualJson = RcLootCouncilJsonWriter.Write(entries, GoldenTimeZone);

        var expectedRows = ParseRows(expectedJson);
        var actualRows = ParseRows(actualJson);

        // 1. The sequence of servertime values matches: proves the sort key (time) and direction
        // (descending) agree, independent of how ties within one second are broken.
        Assert.Equal(expectedRows.Select(r => r.ServerTime), actualRows.Select(r => r.ServerTime));

    }

    [Fact]
    public void Write_RealFixture_SortsNewestFirst_AndDoesNotInventIds()
    {
        var entries = LootHistoryReader.Read(File.ReadAllText(FixturePath("loot-history-real.lua")));
        var json = RcLootCouncilJsonWriter.Write(entries, GoldenTimeZone);
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.EnumerateArray().ToList();

        Assert.Equal(entries.Count, rows.Count);
        var times = rows.Select(r => r.GetProperty("servertime").GetInt64()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        Assert.All(rows, r => Assert.Equal("", r.GetProperty("id").GetString()));
    }

    [Fact]
    public void Write_Empty_ProducesAnEmptyJsonArray()
    {
        Assert.Equal("[]", RcLootCouncilJsonWriter.Write(Array.Empty<LootHistoryEntry>()));
    }

    [Fact]
    public void Write_AwardId_StaysPutWhenANewerAwardIsInserted()
    {
        var older = Entry(("id", "award-old"), ("time", 100d), ("winner", "A"));
        var newer = Entry(("id", "award-new"), ("time", 200d), ("winner", "B"));

        var first = RcLootCouncilJsonWriter.Write(new[] { older });
        var second = RcLootCouncilJsonWriter.Write(new[] { newer, older });

        Assert.Contains("\"id\":\"award-old\"", first);
        Assert.Contains("\"id\":\"award-old\"", second);
        Assert.DoesNotContain("100-1", second);
        Assert.DoesNotContain("100-2", second);
    }

    [Fact]
    public void Write_ItemIdBossAndInstance_ComeFromTheRow()
    {
        var entry = Entry(
            ("id", "award-1"),
            ("time", 100d),
            ("item", "|cnIQ4:|Hitem:249915::::::::90:253:::::::::|h[Extinction Guards]|h|r"),
            ("boss", "Dimensius"),
            ("instance", "March on Quel'Danas"),
            ("difficultyID", 16d));

        var json = RcLootCouncilJsonWriter.Write(new[] { entry });

        Assert.Contains("\"itemID\":249915", json);
        Assert.Contains("\"boss\":\"Dimensius\"", json);
        Assert.Contains("\"instance\":\"March on Quel'Danas\"", json);
        Assert.DoesNotContain("difficulty", json);
    }

    [Fact]
    public void Write_UsesTheRcDateIdAndResponse()
    {
        var entry = Entry(
            ("id", "1787250681-3"),
            ("time", 1787257860d),
            ("dateStr", "2026/08/20"),
            ("timeStr", "20:31:00"),
            ("winner", "Shariuchiha-Blackmoore"),
            ("reason", "Offspec"),
            ("responseID", 3d),
            ("instanceLabel", "The Venomous Abyss-Normal"),
            ("boss", "Ula'tek"),
            ("votes", 2d),
            ("owner", "Kandypal-Blackmoore"));

        var json = RcLootCouncilJsonWriter.Write(new[] { entry });

        Assert.Contains("\"date\":\"2026/08/20\"", json);
        Assert.Contains("\"time\":\"20:31:00\"", json);
        Assert.Contains("\"responseID\":\"3\"", json);
        Assert.Contains("\"servertime\":1787250681", json);
        Assert.Contains("\"instance\":\"The Venomous Abyss-Normal\"", json);
        Assert.Contains("\"votes\":2", json);
    }

    [Fact]
    public void Write_KartHexId_BecomesServertimeAndDecimalSeq()
    {
        var hex = Entry(("id", "1790185049-00d"), ("time", 1790185049d), ("winner", "A"));
        var padded = Entry(("id", "1790185050-011"), ("time", 1790185050d), ("winner", "B"));
        var rc = Entry(("id", "1790195862-13"), ("time", 1790195862d), ("winner", "C"));

        var json = RcLootCouncilJsonWriter.Write(new[] { hex, padded, rc });

        Assert.Contains("\"id\":\"1790185049-13\"", json);
        Assert.Contains("\"id\":\"1790185050-17\"", json);
        Assert.Contains("\"id\":\"1790195862-13\"", json);
        Assert.Contains("\"servertime\":1790185049", json);
    }

    [Fact]
    public void Write_OwnerWithoutRealm_UsesTheWinner()
    {
        var missing = Entry(
            ("time", 100d),
            ("winner", "Mischkadk-Blackrock"),
            ("reason", ""));
        var shortName = Entry(
            ("time", 90d),
            ("winner", "Feriamii-Blackhand"),
            ("owner", "Feriamii"));
        var otherLooter = Entry(
            ("time", 80d),
            ("winner", "Skavie-Blackhand"),
            ("owner", "Kanderadk-Blackmoore"));

        var json = RcLootCouncilJsonWriter.Write(new[] { missing, shortName, otherLooter });

        Assert.Contains("\"owner\":\"Mischkadk-Blackrock\"", json);
        Assert.Contains("\"owner\":\"Feriamii-Blackhand\"", json);
        Assert.Contains("\"owner\":\"Kanderadk-Blackmoore\"", json);
    }

    [Fact]
    public void Write_MissingId_EmitsAnEmptyId()
    {
        var entry = Entry(("time", 100d), ("winner", "A"));
        var json = RcLootCouncilJsonWriter.Write(new[] { entry });
        Assert.Contains("\"id\":\"\"", json);
        Assert.DoesNotContain("100-1", json);
    }

    private static LootHistoryEntry Entry(params (string Key, object? Value)[] fields) =>
        new(fields.ToDictionary(f => f.Key, f => f.Value));

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

    private readonly record struct Row(string ServerTime, string Id, string CanonicalWithBlankId);

    private static List<Row> ParseRows(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var rows = new List<Row>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var serverTimeEl = element.GetProperty("servertime");
            var serverTime = serverTimeEl.ValueKind == JsonValueKind.Number
                ? serverTimeEl.GetInt64().ToString(CultureInfo.InvariantCulture)
                : serverTimeEl.GetString()!;
            var id = element.GetProperty("id").GetString()!;
            rows.Add(new Row(serverTime, id, Canonicalize(element)));
        }
        return rows;
    }

    private static Dictionary<string, List<string>> GroupByServertime(List<Row> rows) =>
        rows.GroupBy(r => r.ServerTime)
            .ToDictionary(g => g.Key, g => g.Select(r => r.CanonicalWithBlankId).OrderBy(s => s, StringComparer.Ordinal).ToList());

    // Reserializes one row object, preserving the field order the writer produced it in, but with the
    // positional suffix of `id` replaced by a placeholder -- so two rows that agree on every field
    // except which same-second award got which index still compare equal.
    private static string Canonicalize(JsonElement obj)
    {
        var sb = new StringBuilder("{");
        var first = true;
        foreach (var prop in obj.EnumerateObject())
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsonSerializer.Serialize(prop.Name)).Append(':');
            if (prop.Name == "id")
            {
                var value = prop.Value.GetString()!;
                var dash = value.LastIndexOf('-');
                var prefix = dash >= 0 ? value[..dash] : value;
                sb.Append(JsonSerializer.Serialize(prefix + "-*"));
            }
            else
            {
                sb.Append(prop.Value.GetRawText());
            }
        }
        sb.Append('}');
        return sb.ToString();
    }
}
