using KARTCompanion.Archive;

namespace KARTCompanion.Export;

public enum PreviewClass { Duplicate, New, Invalid, TooOld }

public sealed record UploadPreview(bool ClassifiedPerId, IReadOnlyDictionary<string, PreviewClass> ById);

public sealed record UploadDecision(
    bool Stopped,
    IReadOnlyList<string> ReceiptIds,
    IReadOnlyList<string> ConfirmIds,
    string Message);

public static class WowUtilsUploadPlanner
{
    private const string UnclassifiedMessage = "WowUtils did not classify every award. Nothing was imported.";
    private const string PreviousImportNewMessage = "A previous import came back as new. Nothing was imported.";

    public static IReadOnlyList<ArchivedAward> SelectForPreview(IEnumerable<ArchivedAward> awards) =>
        awards.Where(a => !a.Withdrawn && !a.ExcludedFromExport && !IsBonusRoll(a)
            && !string.IsNullOrEmpty(EffectiveId(a))).ToList();

    public static int CountMissingId(IEnumerable<ArchivedAward> awards) =>
        awards.Count(a => !a.Withdrawn && !a.ExcludedFromExport && !IsBonusRoll(a)
            && string.IsNullOrEmpty(EffectiveId(a)));

    public static UploadDecision Decide(IReadOnlyList<ArchivedAward> previewed, UploadPreview preview, int skippedMissingId)
    {
        if (!preview.ClassifiedPerId)
            return Stopped();

        foreach (var award in previewed)
        {
            var id = EffectiveId(award);
            if (id == null || !preview.ById.ContainsKey(id))
                return Stopped();
        }

        foreach (var award in previewed)
        {
            var id = EffectiveId(award)!;
            if (preview.ById[id] == PreviewClass.New &&
                (award.WowUtilsImportedAt != null || award.WowUtilsConfirmUncertainAt != null))
                return Stopped(PreviousImportNewMessage);
        }

        var receiptIds = new List<string>();
        var confirmIds = new List<string>();

        foreach (var award in previewed)
        {
            var id = EffectiveId(award)!;
            var classification = preview.ById[id];

            if (classification == PreviewClass.Duplicate && award.WowUtilsImportedAt == null)
                receiptIds.Add(id);
            else if (classification == PreviewClass.New &&
                     award.WowUtilsImportedAt == null &&
                     award.WowUtilsConfirmUncertainAt == null)
                confirmIds.Add(id);
        }

        return new UploadDecision(
            false,
            receiptIds,
            confirmIds,
            $"Ready to import {confirmIds.Count}. {receiptIds.Count} already in WowUtils. {skippedMissingId} skipped with no id.");
    }

    public static void ApplyReceipts(IEnumerable<ArchivedAward> awards, IReadOnlyList<string> ids, DateTimeOffset now)
    {
        var idSet = ids.ToHashSet();
        foreach (var award in awards)
        {
            var id = EffectiveId(award);
            if (id == null || !idSet.Contains(id)) continue;

            award.WowUtilsImportedAt = now;
            award.WowUtilsConfirmUncertainAt = null;
        }
    }

    public static void MarkUncertain(IEnumerable<ArchivedAward> awards, IReadOnlyList<string> ids, DateTimeOffset now)
    {
        var idSet = ids.ToHashSet();
        foreach (var award in awards)
        {
            var id = EffectiveId(award);
            if (id == null || !idSet.Contains(id)) continue;

            award.WowUtilsConfirmUncertainAt = now;
        }
    }

    // Same rule as IsBonusEntry in KeineAhnungRaidTools LootHistory.lua. A missing or
    // non-string responseID is not a bonus roll.
    private static bool IsBonusRoll(ArchivedAward award)
    {
        var responseId = award.EffectiveFields.TryGetValue("responseID", out var v) ? v as string : null;
        return responseId is "BONUSROLL" or "BONUS_ROLL";
    }

    private static string? EffectiveId(ArchivedAward award) =>
        award.EffectiveFields.TryGetValue("id", out var v) ? v as string : null;

    private static UploadDecision Stopped(string? message = null) =>
        new(true, Array.Empty<string>(), Array.Empty<string>(), message ?? UnclassifiedMessage);
}
