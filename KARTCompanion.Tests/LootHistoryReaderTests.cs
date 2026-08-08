using KARTCompanion.SavedVariables;

namespace KARTCompanion.Tests;

public class LootHistoryReaderTests
{
    private static string FixtureText() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "loot-history-real.lua"));

    // The fixture is a real file WoW wrote, anonymised only in the two identity fields. A fixture
    // written by hand would only prove the reader understands the author's idea of the format —
    // and that idea is exactly what can be wrong.
    [Fact]
    public void Read_RealFile_FindsEveryAward()
    {
        var entries = LootHistoryReader.Read(FixtureText());

        Assert.Equal(133, entries.Count);
    }

    // color and difficultyID are ABSENT on some entries, not null. A reader that invents them
    // turns "we never knew" into "we know it was nothing", which the archive then stores forever.
    [Fact]
    public void Read_RealFile_KeepsOptionalFieldsAbsentRatherThanNull()
    {
        var entries = LootHistoryReader.Read(FixtureText());

        Assert.Equal(104, entries.Count(e => e.Fields.ContainsKey("color")));
        Assert.Equal(102, entries.Count(e => e.Fields.ContainsKey("difficultyID")));
        Assert.All(entries, e => Assert.True(e.Fields.ContainsKey("time")));
    }

    // The test above only asks whether the key is there. A reader that produced an EMPTY dictionary
    // for every one of those 104 colors would satisfy it — and `color` is precisely the field that
    // produced this project's nested-table Critical, where the plan claimed nested tables did not
    // exist at all. So pin the CONTENT: the first award's real r/g/b out of the fixture, and the
    // fact that the file carries more than one distinct color, which no constant placeholder has.
    [Fact]
    public void Read_RealFile_ReadsNestedTableContentNotJustItsPresence()
    {
        var entries = LootHistoryReader.Read(FixtureText());

        var color = Assert.IsType<Dictionary<string, object?>>(entries[0].Fields["color"]);
        Assert.Equal(0.8470588235294118, Assert.IsType<double>(color["r"]));
        Assert.Equal(0.4980392156862745, Assert.IsType<double>(color["g"]));
        Assert.Equal(1.0, Assert.IsType<double>(color["b"]));

        var distinctReds = entries
            .Where(e => e.Fields.ContainsKey("color"))
            .Select(e => ((Dictionary<string, object?>)e.Fields["color"]!)["r"])
            .Distinct()
            .Count();
        Assert.True(distinctReds > 1, $"Expected several distinct class colors, found {distinctReds}.");
    }

    // This file predates the award id. The re-review found these 133 rows are not 133 distinct
    // awards under any key — they are the same award observed once per syncing client (see
    // ArchiveMergerTests and the round-3 report for the evidence). The maintainer ruled they are not
    // archived at all: ArchiveMerger skips any entry without an id. What's worth pinning here is that
    // the reader still parses every one of these 133 entries correctly, and that the absence of an
    // id is real — the fact that makes them unarchivable, not a reader bug.
    [Fact]
    public void Read_RealFile_PreReleaseEntriesHaveNoId()
    {
        var entries = LootHistoryReader.Read(FixtureText());

        Assert.Equal(133, entries.Count);
        Assert.All(entries, e => Assert.Null(e.Id));
    }

    [Fact]
    public void Read_BlockAbsent_ReturnsEmpty()
    {
        var entries = LootHistoryReader.Read("KART_Settings = {\n[\"a\"] = 1,\n}\n");

        Assert.Empty(entries);
    }

    // Half a file is the dangerous case: partially read data is indistinguishable from real data
    // once it is in the archive. Refusing the whole pass is the only safe answer.
    [Fact]
    public void Read_TruncatedBlock_Throws()
    {
        var text = "KART_LootHistory = {\n{\n[\"time\"] = 1785356434,\n[\"winner\"] = \"Raider01\",\n";

        Assert.Throws<FormatException>(() => LootHistoryReader.Read(text));
    }

    // The test above ends MID-award, so the end-of-method "ended mid-award" guard catches it and the
    // missing-closing-brace guard is never what fails — delete that guard and the test still passes.
    // The physically likelier truncation is this one: a read that lands just after a "}," line yields
    // a syntactically COMPLETE but short history. Without the missing-brace guard the reader hands it
    // back as if it were the whole file, the newest awards look as though they never existed, and the
    // merger reads real awards as withdrawn.
    [Fact]
    public void Read_TruncatedOnAnAwardBoundary_Throws()
    {
        var text = "KART_LootHistory = {\n{\n[\"time\"] = 1,\n[\"id\"] = \"a\",\n},\n";

        Assert.Throws<FormatException>(() => LootHistoryReader.Read(text));
    }

    // An addon version newer than this build may add a field. Dropping it would make the archive
    // lossy against a file it could read perfectly well.
    [Fact]
    public void Read_UnknownField_IsKept()
    {
        var text = "KART_LootHistory = {\n{\n[\"time\"] = 1,\n[\"somethingNew\"] = \"x\",\n},\n}\n";

        var entries = LootHistoryReader.Read(text);

        Assert.Equal("x", entries.Single().Fields["somethingNew"]);
    }

    // A bare, unquoted, non-numeric token is not a shape WoW's serializer ever writes. Silently
    // turning it into null would be the same "we never knew" -> "we know it was nothing" mistake
    // the optional-field tests guard against, just reached through a different door.
    [Fact]
    public void Read_UnrecognisedValue_Throws()
    {
        var text = "KART_LootHistory = {\n{\n[\"time\"] = 1,\n[\"weird\"] = notaliteral,\n},\n}\n";

        Assert.Throws<FormatException>(() => LootHistoryReader.Read(text));
    }
}
