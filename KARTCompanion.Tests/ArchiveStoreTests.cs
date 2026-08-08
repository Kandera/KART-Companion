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
}
