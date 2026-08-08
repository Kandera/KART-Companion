using KARTCompanion.Archive;

namespace KARTCompanion.Tests;

public class AwardEditorTests
{
    private static ArchivedAward Award(string winner = "Bramblewick", string? reason = "BIS")
    {
        var fields = new Dictionary<string, object?> { ["id"] = "a1", ["winner"] = winner };
        if (reason != null) fields["reason"] = reason;
        return new ArchivedAward { Fields = fields };
    }

    [Fact]
    public void Set_CorrectsTheEffectiveValueAndLeavesFieldsAlone()
    {
        var award = Award();

        AwardEditor.Set(award, "winner", "Thornfell");

        Assert.Equal("Thornfell", award.EffectiveFields["winner"]);
        Assert.Equal("Bramblewick", award.Fields["winner"]);
        Assert.True(AwardEditor.IsEdited(award));
        Assert.True(AwardEditor.IsFieldEdited(award, "winner"));
        Assert.False(AwardEditor.IsFieldEdited(award, "reason"));
    }

    [Fact]
    public void Set_TrimsSurroundingWhitespace()
    {
        var award = Award();

        AwardEditor.Set(award, "winner", "  Thornfell  ");

        Assert.Equal("Thornfell", award.EffectiveFields["winner"]);
    }

    // Typing the addon's own value back in is not a correction. Without this, opening the dialog and
    // pressing Save would mark every award it touched as edited forever — and "edited" is meant to
    // mean "the maintainer asserted this", not "the dialog was opened".
    [Fact]
    public void Set_ValueEqualToTheAddonsOwn_RecordsNoEdit()
    {
        var award = Award();

        AwardEditor.Set(award, "winner", "Bramblewick");

        Assert.False(AwardEditor.IsEdited(award));
        Assert.Empty(award.Edits);
    }

    // Same rule where the addon wrote nothing at all: blank in, no edit recorded, and the field stays
    // ABSENT rather than becoming present-and-empty. LootHistoryEntry's own remarks are about exactly
    // this distinction.
    [Fact]
    public void Set_BlankOnAFieldTheAddonNeverWrote_RecordsNoEdit()
    {
        var award = Award(reason: null);

        AwardEditor.Set(award, "reason", "   ");

        Assert.False(AwardEditor.IsEdited(award));
        Assert.False(award.EffectiveFields.ContainsKey("reason"));
    }

    // Clearing an edit is a Set back to the original, not a special path.
    [Fact]
    public void Set_BackToTheOriginal_RemovesTheEdit()
    {
        var award = Award();
        AwardEditor.Set(award, "winner", "Thornfell");

        AwardEditor.Set(award, "winner", "Bramblewick");

        Assert.False(AwardEditor.IsEdited(award));
        Assert.Equal("Bramblewick", award.EffectiveFields["winner"]);
    }

    [Fact]
    public void Revert_DropsEveryEditAndRestoresTheAddonsValues()
    {
        var award = Award();
        AwardEditor.Set(award, "winner", "Thornfell");
        AwardEditor.Set(award, "reason", "Offspec");

        AwardEditor.Revert(award);

        Assert.False(AwardEditor.IsEdited(award));
        Assert.Equal("Bramblewick", award.EffectiveFields["winner"]);
        Assert.Equal("BIS", award.EffectiveFields["reason"]);
    }

    // A field the addon never wrote must come back ABSENT after a revert, not present-and-empty.
    [Fact]
    public void Revert_RestoresAnAbsentFieldAsAbsent()
    {
        var award = Award(reason: null);
        AwardEditor.Set(award, "reason", "Offspec");
        Assert.Equal("Offspec", award.EffectiveFields["reason"]);

        AwardEditor.Revert(award);

        Assert.False(award.EffectiveFields.ContainsKey("reason"));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("epoch")]
    [InlineData("time")]
    [InlineData("item")]
    [InlineData("exported")]
    [InlineData("winnerKey")]
    public void Set_RefusesEveryFieldOutsideTheAllowlist(string field)
    {
        var award = Award();

        Assert.Throws<ArgumentException>(() => AwardEditor.Set(award, field, "anything"));
        Assert.Empty(award.Edits);
    }

    // Pinned as a list, not just by the refusals above: a test that only proves the two work would
    // pass with the editor wide open, and a test that only proves six names are refused would pass if
    // a seventh were quietly allowed.
    [Fact]
    public void EditableFields_IsExactlyWinnerAndReason()
    {
        Assert.Equal(new[] { "winner", "reason" }, AwardEditor.EditableFields);
    }

    private static ArchiveDocument DocumentWith(params ArchivedAward[] awards)
    {
        var doc = new ArchiveDocument();
        doc.Awards.AddRange(awards);
        return doc;
    }

    private static ArchivedAward AwardWithId(string id, string winner = "Bramblewick") =>
        new() { Fields = new Dictionary<string, object?> { ["id"] = id, ["winner"] = winner, ["reason"] = "BIS" } };

    [Fact]
    public void ApplyAndSave_AppliesToTheFreshlyLoadedDocumentAndSavesIt()
    {
        var fresh = DocumentWith(AwardWithId("a1"));
        ArchiveDocument? saved = null;

        var outcome = AwardEditor.ApplyAndSave(
            "a1",
            new Dictionary<string, string> { ["winner"] = "Thornfell", ["reason"] = "Offspec" },
            excludedFromExport: false,
            () => fresh,
            doc => saved = doc);

        Assert.Same(fresh, outcome.Document);
        Assert.Same(fresh, saved);
        Assert.Equal("Thornfell", outcome.Award.EffectiveFields["winner"]);
        Assert.Equal("Offspec", outcome.Award.EffectiveFields["reason"]);
    }

    // The caller holds a display snapshot that can be minutes old, and a merge may have added awards
    // to the file since. Editing the caller's object and saving the caller's document would drop them.
    [Fact]
    public void ApplyAndSave_DoesNotLoseAwardsAddedSinceTheCallersSnapshot()
    {
        var stale = AwardWithId("a1");
        var fresh = DocumentWith(AwardWithId("a1"), AwardWithId("a2", "Marrowlight"));
        ArchiveDocument? saved = null;

        AwardEditor.ApplyAndSave(
            stale.Key,
            new Dictionary<string, string> { ["winner"] = "Thornfell" },
            excludedFromExport: false,
            () => fresh,
            doc => saved = doc);

        Assert.Equal(2, saved!.Awards.Count);
        Assert.Equal("Thornfell", saved.Awards.Single(a => a.Key == "a1").EffectiveFields["winner"]);
        // The caller's own object is not what was edited.
        Assert.False(AwardEditor.IsEdited(stale));
    }

    [Fact]
    public void ApplyAndSave_SetsAndClearsTheExclusion()
    {
        var fresh = DocumentWith(AwardWithId("a1"));

        var excluded = AwardEditor.ApplyAndSave(
            "a1", new Dictionary<string, string>(), excludedFromExport: true, () => fresh, _ => { });
        Assert.True(excluded.Award.ExcludedFromExport);

        var included = AwardEditor.ApplyAndSave(
            "a1", new Dictionary<string, string>(), excludedFromExport: false, () => fresh, _ => { });
        Assert.False(included.Award.ExcludedFromExport);
    }

    // If the award is not there, the archive is not the one the caller was looking at — the likeliest
    // cause is that it was quarantined and Load answered a fresh empty document. Saving then would
    // write an empty archive over the only copy of history the game has already forgotten.
    [Fact]
    public void ApplyAndSave_UnknownKey_ThrowsAndSavesNothing()
    {
        var saveCalled = false;

        Assert.Throws<InvalidOperationException>(() => AwardEditor.ApplyAndSave(
            "missing",
            new Dictionary<string, string> { ["winner"] = "Thornfell" },
            excludedFromExport: false,
            () => new ArchiveDocument(),
            _ => saveCalled = true));

        Assert.False(saveCalled);
    }

    [Fact]
    public void ApplyAndSave_RefusedField_ThrowsAndSavesNothing()
    {
        var fresh = DocumentWith(AwardWithId("a1"));
        var saveCalled = false;

        Assert.Throws<ArgumentException>(() => AwardEditor.ApplyAndSave(
            "a1",
            new Dictionary<string, string> { ["id"] = "a2" },
            excludedFromExport: false,
            () => fresh,
            _ => saveCalled = true));

        Assert.False(saveCalled);
    }
}
