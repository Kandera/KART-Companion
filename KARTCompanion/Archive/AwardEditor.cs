namespace KARTCompanion.Archive;

/// <summary>
/// The maintainer's corrections to an archived award.
///
/// This exists for a narrow purpose: for two or three weeks after the addon changes of sub-projects 1
/// and 2, a defect could log an award wrongly, and what reaches WoWUtils should stay clean regardless.
/// It corrects the archive only. Nothing here writes towards the game — see the design spec for why
/// pushing a correction back into the game's saved variables was examined and dropped.
///
/// An edit never touches ArchivedAward.Fields; see the remarks on ArchivedAward.Edits for why that
/// placement is the whole design.
/// </summary>
public static class AwardEditor
{
    /// <summary>
    /// The only two fields an award can be corrected in: who got the item, and why.
    ///
    /// Deliberately not "every field the addon writes". Asked what would actually be corrected, the
    /// maintainer named these two — and said that an award a defect made wrong in any other way simply
    /// gets dropped from the export rather than repaired by hand (see
    /// ArchivedAward.ExcludedFromExport). That fallback is what lets this list be short: it covers
    /// everything not on it.
    ///
    /// `id` and `epoch` would be excluded even if the list were wide. They are how the archive
    /// recognises an award and how ArchiveMerger derives a withdrawal, not statements about the award.
    /// Editing `id` orphans the row: the next snapshot no longer matches it, the merge re-adds the
    /// original, and the archive holds two.
    ///
    /// Widening this later is a line of code for the rule everything rests on — the original is kept —
    /// which does not depend on how many fields are editable. It is not a line of code for the feature
    /// as a whole: a third field also needs a box in AwardEditDialog, an entry in EditSelected's
    /// hard-coded dictionary in HistoryScreen.cs, a case in HistoryExportPlanner.FieldForColumn, and a
    /// line in EditSummary — and missing any one of those four is silent, not a red test: the field
    /// would simply stay uneditable from the UI and never highlighted.
    /// </summary>
    public static readonly IReadOnlyList<string> EditableFields = new[] { "winner", "reason" };

    /// <summary>
    /// Corrects one field. Setting it back to what the addon wrote removes the correction rather than
    /// recording a no-op one: "edited" has to mean the maintainer asserted something, otherwise every
    /// award whose dialog was ever opened and saved would be marked as an assertion forever.
    /// </summary>
    /// <exception cref="ArgumentException">The field is not one of <see cref="EditableFields"/>.</exception>
    public static void Set(ArchivedAward award, string field, string value)
    {
        if (!EditableFields.Contains(field))
            throw new ArgumentException(
                $"'{field}' cannot be edited. Only {string.Join(" and ", EditableFields)} can.", nameof(field));

        var corrected = value.Trim();
        var original = award.Fields.TryGetValue(field, out var v) ? v as string : null;

        // An absent field and an empty one are the same thing to type into a text box, so both compare
        // against "". Removing the edit (rather than storing "") is what keeps an absent field absent.
        if (corrected == (original ?? "")) award.Edits.Remove(field);
        else award.Edits[field] = corrected;
    }

    /// <summary>Drops every correction on this award. What is left is exactly what the addon wrote,
    /// including fields it never wrote at all — those become absent again, not present-and-empty.
    ///
    /// No production caller today: the dialog's "Use the addon's values" button refills the text boxes
    /// instead and lets <see cref="Set"/> clear the edits on Save, reaching the same state. Kept for
    /// its test, which pins the absent-not-empty behaviour above.</summary>
    public static void Revert(ArchivedAward award) => award.Edits.Clear();

    /// <summary>No production caller today — HistoryExportPlanner.EmphasisFor and EditSummary both ask
    /// per-field, via <see cref="IsFieldEdited"/>, because a corrected cell is marked at the field it
    /// applies to, not at the whole award. Kept for its test, which pins that an award with no
    /// corrections at all reports false.</summary>
    public static bool IsEdited(ArchivedAward award) => award.Edits.Count > 0;

    public static bool IsFieldEdited(ArchivedAward award, string field) => award.Edits.ContainsKey(field);

    /// <summary>What an edit did: the document it was applied in, and the award as it now stands.</summary>
    public sealed record EditOutcome(ArchiveDocument Document, ArchivedAward Award);

    /// <summary>
    /// The whole edit path in one tested place: load the archive fresh, apply the corrections to the
    /// award in THAT document, save it.
    ///
    /// The reload is not a nicety. The window is modeless, and while it sits open the tray's "Read
    /// loot history now" and the background sync merge into their own document and save it. Editing
    /// the object the window is holding and saving the window's document would silently drop every
    /// award that arrived in between — from the one file in this program that cannot be regenerated.
    /// HistoryExportPlanner.ExportAndStamp solves the same problem the same way, and for the same
    /// reason.
    ///
    /// Nothing is saved unless every correction was accepted: a refused field throws before save is
    /// ever called, and the freshly-loaded document is discarded with the exception. There is nothing
    /// to roll back, because nothing durable and nothing the caller already held was touched.
    /// </summary>
    /// <param name="key">The award's id. A key rather than the object, because the caller's object
    /// belongs to a snapshot that may already be out of date.</param>
    /// <param name="edits">The complete corrected value for each field being set. A value equal to
    /// what the addon wrote clears that correction — see <see cref="Set"/>.</param>
    public static EditOutcome ApplyAndSave(
        string key,
        IReadOnlyDictionary<string, string> edits,
        bool excludedFromExport,
        Func<ArchiveDocument> load,
        Action<ArchiveDocument> save)
    {
        var fresh = load();

        var award = fresh.Awards.FirstOrDefault(a => a.Key == key)
            ?? throw new InvalidOperationException(
                $"Award {key} is no longer in the archive, so nothing was saved.");

        foreach (var (field, value) in edits) Set(award, field, value);
        award.ExcludedFromExport = excludedFromExport;

        save(fresh);
        return new EditOutcome(fresh, award);
    }
}
