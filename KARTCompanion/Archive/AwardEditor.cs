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
    /// Widening this later is a line of code — the rule everything rests on (the original is kept) does
    /// not depend on how many fields are editable.
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
    /// including fields it never wrote at all — those become absent again, not present-and-empty.</summary>
    public static void Revert(ArchivedAward award) => award.Edits.Clear();

    public static bool IsEdited(ArchivedAward award) => award.Edits.Count > 0;

    public static bool IsFieldEdited(ArchivedAward award, string field) => award.Edits.ContainsKey(field);
}
