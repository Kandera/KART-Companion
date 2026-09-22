using System.Text.Json;
using KARTCompanion.Archive;
using KARTCompanion.SavedVariables;
using KARTCompanion.WowUtils;

namespace KARTCompanion.Export;

public enum ExportResultKind
{
    Success,
    Error,
}

public sealed record ExportResult(ExportResultKind Kind, string Message);

public static class WowUtilsExportService
{
    private const string NotCapturedMessage = "WowUtils import is not captured yet. Nothing was imported.";
    private const string LoginMessage = "Paste the WowUtils login in Settings. Nothing was imported.";
    private const string ConfirmMismatchMessage = "WowUtils did not name the imported awards. Nothing further was imported.";

    public static async Task<ExportResult> Run(
        Func<ArchiveDocument> load,
        Action<ArchiveDocument> save,
        Action? refresh,
        ILootHistoryImportTransport transport,
        bool uploadEnabled,
        bool hasCredential,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!uploadEnabled)
            return Fail(NotCapturedMessage);

        if (!hasCredential)
            return Fail(LoginMessage);

        refresh?.Invoke();
        var doc = load();

        var previewed = WowUtilsUploadPlanner.SelectForPreview(doc.Awards);
        var skipped = WowUtilsUploadPlanner.CountMissingId(doc.Awards);

        var previewJson = RcLootCouncilJsonWriter.Write(previewed.Select(a => new LootHistoryEntry(a.EffectiveFields)));
        var previewResult = await transport.PreviewAsync(previewJson, cancellationToken);

        if (previewResult.StatusCode is 401 or 403)
            return Fail(LoginMessage);

        var preview = PreviewBodyParser.Parse(previewResult.Body);
        var decision = WowUtilsUploadPlanner.Decide(previewed, preview, skipped);

        if (decision.Stopped)
            return Fail(decision.Message);

        WowUtilsUploadPlanner.ApplyReceipts(doc.Awards, decision.ReceiptIds, now);

        // Nothing left to confirm: duplicates already stamped, or there was nothing new to send.
        if (decision.ConfirmIds.Count == 0)
        {
            save(doc);
            return Ok(decision.Message);
        }

        WowUtilsUploadPlanner.MarkUncertain(doc.Awards, decision.ConfirmIds, now);
        save(doc);

        var confirmAwards = previewed.Where(a => decision.ConfirmIds.Contains(EffectiveId(a)!)).ToList();
        var confirmJson = RcLootCouncilJsonWriter.Write(confirmAwards.Select(a => new LootHistoryEntry(a.EffectiveFields)));
        var confirmResult = await transport.ConfirmAsync(confirmJson, cancellationToken);

        if (confirmResult.StatusCode is 401 or 403)
            return Fail(LoginMessage);

        if (confirmResult.StatusCode != 200 ||
            !TryParseAccepted(confirmResult.Body, out var accepted) ||
            !ConfirmSetMatches(decision.ConfirmIds, accepted))
            return Fail(ConfirmMismatchMessage);

        WowUtilsUploadPlanner.ApplyReceipts(doc.Awards, accepted, now);
        save(doc);
        return Ok(decision.Message);
    }

    private static ExportResult Ok(string message) => new(ExportResultKind.Success, message);

    private static ExportResult Fail(string message) => new(ExportResultKind.Error, message);

    private static string? EffectiveId(ArchivedAward award) =>
        award.EffectiveFields.TryGetValue("id", out var v) ? v as string : null;

    private static bool TryParseAccepted(string? body, out IReadOnlyList<string> accepted)
    {
        accepted = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("accepted", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return false;

            var ids = new List<string>();
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String)
                    return false;
                var id = el.GetString();
                if (id == null)
                    return false;
                ids.Add(id);
            }

            accepted = ids;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ConfirmSetMatches(IReadOnlyList<string> confirmIds, IReadOnlyList<string> accepted) =>
        confirmIds.Count == accepted.Count && confirmIds.ToHashSet().SetEquals(accepted);
}

public static class PreviewBodyParser
{
    public static UploadPreview Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new UploadPreview(false, new Dictionary<string, PreviewClass>());

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return new UploadPreview(false, new Dictionary<string, PreviewClass>());

            var byId = new Dictionary<string, PreviewClass>();
            foreach (var row in results.EnumerateArray())
            {
                if (!row.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                    return new UploadPreview(false, new Dictionary<string, PreviewClass>());

                var id = idEl.GetString();
                if (id == null)
                    return new UploadPreview(false, new Dictionary<string, PreviewClass>());

                if (!row.TryGetProperty("status", out var statusEl) || statusEl.ValueKind != JsonValueKind.String)
                    return new UploadPreview(false, new Dictionary<string, PreviewClass>());

                if (!TryMapStatus(statusEl.GetString(), out var previewClass))
                    return new UploadPreview(false, new Dictionary<string, PreviewClass>());

                byId[id] = previewClass;
            }

            return new UploadPreview(true, byId);
        }
        catch (JsonException)
        {
            return new UploadPreview(false, new Dictionary<string, PreviewClass>());
        }
    }

    private static bool TryMapStatus(string? status, out PreviewClass previewClass)
    {
        switch (status)
        {
            case "new":
                previewClass = PreviewClass.New;
                return true;
            case "duplicate":
                previewClass = PreviewClass.Duplicate;
                return true;
            case "invalid":
                previewClass = PreviewClass.Invalid;
                return true;
            case "too_old":
                previewClass = PreviewClass.TooOld;
                return true;
            default:
                previewClass = default;
                return false;
        }
    }
}
