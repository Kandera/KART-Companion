using KARTCompanion.Archive;

namespace KARTCompanion.Tests;

public class ArchiveStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kart-archive-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "loot-history.json");

    public ArchiveStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Load_NoFile_ReturnsEmptyDocument()
    {
        var doc = ArchiveStore.Load(Path_);

        Assert.Equal(1, doc.Version);
        Assert.Empty(doc.Awards);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var doc = new ArchiveDocument();
        doc.Awards.Add(new ArchivedAward
        {
            Fields = new Dictionary<string, object?>
            {
                ["id"] = "award-1",
                ["time"] = 1785356434d,
                ["winner"] = "Raider01",
                ["item"] = "|cffa335ee|Hitem:249331::::::::80:::::|h[Gloves]|h|r",
                ["somethingNew"] = "x",
            },
            SourceFile = @"C:\wow\file.lua",
            FirstSeen = DateTimeOffset.FromUnixTimeSeconds(1785356400),
            LastSeen = DateTimeOffset.FromUnixTimeSeconds(1785356500),
        });

        ArchiveStore.Save(doc, Path_);
        var back = ArchiveStore.Load(Path_);

        var award = Assert.Single(back.Awards);
        Assert.Equal("Raider01", award.Fields["winner"]);
        // A field this build does not know must survive the round trip, or the archive is lossy
        // against a file it could read perfectly well.
        Assert.Equal("x", award.Fields["somethingNew"]);
        Assert.Equal(@"C:\wow\file.lua", award.SourceFile);
        Assert.Equal(1785356400, award.FirstSeen.ToUnixTimeSeconds());
    }

    // ConfigStore.Load deliberately starts fresh on a corrupt file. The archive must NOT: it is the
    // one file here that cannot be regenerated, and "start fresh" means losing every award the game
    // has already forgotten.
    [Fact]
    public void Load_CorruptFile_QuarantinesInsteadOfReplacing()
    {
        File.WriteAllText(Path_, "{ this is not json");

        var ex = Assert.Throws<ArchiveUnreadableException>(() => ArchiveStore.Load(Path_));

        Assert.True(File.Exists(ex.QuarantinePath), "the unreadable archive must still exist somewhere");
        Assert.Equal("{ this is not json", File.ReadAllText(ex.QuarantinePath));
        Assert.False(File.Exists(Path_), "the corrupt file is moved, not left in place to be overwritten");
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        ArchiveStore.Save(new ArchiveDocument(), Path_);

        Assert.Single(Directory.GetFiles(_dir));
    }

    // Review finding (Critical 1): the quarantine name must not collide across two calls in the same
    // second, or the second quarantine silently destroys the first one's bytes — the exact loss the
    // quarantine exists to prevent.
    [Fact]
    public void Load_CorruptFile_Twice_DoesNotOverwritePriorQuarantine()
    {
        File.WriteAllText(Path_, "FIRST-CORRUPT");
        var ex1 = Assert.Throws<ArchiveUnreadableException>(() => ArchiveStore.Load(Path_));

        File.WriteAllText(Path_, "SECOND-CORRUPT");
        var ex2 = Assert.Throws<ArchiveUnreadableException>(() => ArchiveStore.Load(Path_));

        Assert.NotEqual(ex1.QuarantinePath, ex2.QuarantinePath);
        Assert.Equal("FIRST-CORRUPT", File.ReadAllText(ex1.QuarantinePath));
        Assert.Equal("SECOND-CORRUPT", File.ReadAllText(ex2.QuarantinePath));
    }

    // Review finding (Critical 2): the reader's nested-table fields (e.g. "color") come through as
    // Dictionary<string, object?> values, not string/double/bool. A field shaped like that must survive
    // the round trip, not come back as null.
    [Fact]
    public void SaveThenLoad_RoundTripsNestedTableField()
    {
        var doc = new ArchiveDocument();
        doc.Awards.Add(new ArchivedAward
        {
            Fields = new Dictionary<string, object?>
            {
                ["id"] = "award-1",
                ["time"] = 1785356434d,
                ["color"] = new Dictionary<string, object?>
                {
                    ["r"] = 1d,
                    ["g"] = 0.498d,
                    ["b"] = 0.847d,
                },
            },
            SourceFile = @"C:\wow\file.lua",
        });

        ArchiveStore.Save(doc, Path_);
        var back = ArchiveStore.Load(Path_);

        var award = Assert.Single(back.Awards);
        var color = Assert.IsType<Dictionary<string, object?>>(award.Fields["color"]);
        Assert.Equal(1d, color["r"]);
        Assert.Equal(0.498d, color["g"]);
        Assert.Equal(0.847d, color["b"]);
    }

    // Review finding (Important 3): a file whose entire content is the JSON literal "null" deserializes
    // without throwing. Left alone, that reintroduces ConfigStore's "start fresh" behaviour through a
    // side door in the one file where it must never happen.
    [Fact]
    public void Load_NullLiteralFile_QuarantinesInsteadOfReplacing()
    {
        File.WriteAllText(Path_, "null");

        var ex = Assert.Throws<ArchiveUnreadableException>(() => ArchiveStore.Load(Path_));

        Assert.True(File.Exists(ex.QuarantinePath), "the unreadable archive must still exist somewhere");
        Assert.False(File.Exists(Path_), "the null-literal file is moved, not left in place");
    }

    // Final review, MINOR 2: Load validated nothing — not even Version. An archive that was
    // hand-edited, or written by an intermediate build, held awards with no id; ArchivedAward.Key
    // then threw on every later pass with only a generic crash balloon, and the feature stopped for
    // good. Fail at load instead, where the file is quarantined intact.
    [Fact]
    public void Load_AwardWithoutId_QuarantinesInsteadOfThrowingOnEveryLaterPass()
    {
        var text = """
            { "Version": 1, "Awards": [ { "Fields": { "time": 1 }, "SourceFile": "C:\\wow\\file.lua" } ] }
            """;
        File.WriteAllText(Path_, text);

        var ex = Assert.Throws<ArchiveUnreadableException>(() => ArchiveStore.Load(Path_));

        Assert.Equal(text, File.ReadAllText(ex.QuarantinePath));
        Assert.False(File.Exists(Path_), "the invalid archive is moved, not left in place");
    }

    [Fact]
    public void Load_UnknownVersion_QuarantinesInsteadOfGuessingAtTheLayout()
    {
        var text = """{ "Version": 99, "Awards": [] }""";
        File.WriteAllText(Path_, text);

        var ex = Assert.Throws<ArchiveUnreadableException>(() => ArchiveStore.Load(Path_));

        Assert.Equal(text, File.ReadAllText(ex.QuarantinePath));
        Assert.False(File.Exists(Path_), "the unknown-version archive is moved, not left in place");
    }

    // Final review: Save overwrote in place with no copy anywhere, and the design's own premise is
    // that this file cannot be regenerated. One generation of history is the cheapest protection
    // available against a bad merge or a bug in a future build.
    [Fact]
    public void Save_OverwritingAnExistingArchive_KeepsThePreviousGenerationAsBak()
    {
        var first = new ArchiveDocument();
        first.Awards.Add(Award("award-1"));
        ArchiveStore.Save(first, Path_);

        var second = new ArchiveDocument();
        second.Awards.Add(Award("award-2"));
        ArchiveStore.Save(second, Path_);

        var backup = ArchiveStore.Load(Path_ + ArchiveStore.BackupSuffix);
        Assert.Equal("award-1", Assert.Single(backup.Awards).Key);
        Assert.Equal("award-2", Assert.Single(ArchiveStore.Load(Path_).Awards).Key);
    }

    private static ArchivedAward Award(string id) => new()
    {
        Fields = new Dictionary<string, object?> { ["id"] = id, ["time"] = 1785356434d },
        SourceFile = @"C:\wow\file.lua",
    };
}
