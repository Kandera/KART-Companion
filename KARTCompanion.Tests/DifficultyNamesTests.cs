using KARTCompanion.SavedVariables;

namespace KARTCompanion.Tests;

public class DifficultyNamesTests
{
    private static LootHistoryEntry Entry(long? difficultyId = null, string? difficulty = null)
    {
        var fields = new Dictionary<string, object?>();
        if (difficultyId != null) fields["difficultyID"] = (double)difficultyId.Value;
        if (difficulty != null) fields["difficulty"] = difficulty;
        return new LootHistoryEntry(fields);
    }

    // The one rule both the export and the list now share: a known difficultyID always wins, even
    // over a stored (and possibly localized) string — a German client's "Mythisch" must not survive
    // into the canonical name.
    [Fact]
    public void Canonical_KnownDifficultyId_ReturnsTheEnglishNameOverTheStoredString()
    {
        var entry = Entry(difficultyId: 16, difficulty: "Mythisch");

        Assert.Equal("Mythic", DifficultyNames.Canonical(entry));
    }

    // An id this table does not (yet) know falls back to whatever string the client stored, exactly
    // as the addon's own DifficultyExport does.
    [Fact]
    public void Canonical_UnknownDifficultyId_FallsBackToTheStoredString()
    {
        var entry = Entry(difficultyId: 9999, difficulty: "Some Future Mode");

        Assert.Equal("Some Future Mode", DifficultyNames.Canonical(entry));
    }

    // Entries logged before difficultyID existed carry only the stored string.
    [Fact]
    public void Canonical_NoDifficultyId_FallsBackToTheStoredString()
    {
        var entry = Entry(difficultyId: null, difficulty: "Heroic");

        Assert.Equal("Heroic", DifficultyNames.Canonical(entry));
    }

    [Fact]
    public void Canonical_NeitherFieldPresent_ReturnsEmptyString()
    {
        Assert.Equal("", DifficultyNames.Canonical(Entry()));
    }
}
