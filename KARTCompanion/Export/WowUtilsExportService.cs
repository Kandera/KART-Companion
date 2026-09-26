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
        if (previewed.Count == 0)
        {
            save(doc);
            return Ok($"Imported 0, updated 0. {skipped} skipped with no id.");
        }

        var entries = RcLootCouncilJsonWriter.Write(previewed.Select(a => new LootHistoryEntry(a.EffectiveFields)));
        var sessionId = Guid.NewGuid().ToString("D");
        var body = "{\"chunkIndex\":0,\"entries\":" + entries + ",\"sessionId\":\"" + sessionId + "\",\"totalChunks\":1}";
        var result = await transport.PreviewAsync(body, cancellationToken);

        if (result.StatusCode is 401 or 403)
            return Fail(LoginMessage);

        if (result.StatusCode != 200 || !TryParseChunkResult(result.Body, out var inserted, out var modified, out var skippedReasons))
            return Fail(Unclassified(result));

        if (skippedReasons.Length > 0)
            return Fail("WowUtils skipped rows. " + skippedReasons);

        var ids = previewed.Select(EffectiveId).Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToList();
        WowUtilsUploadPlanner.ApplyReceipts(doc.Awards, ids, now);
        save(doc);
        return Ok($"Imported {inserted}, updated {modified}. {skipped} skipped with no id.");
    }

    private static bool TryParseChunkResult(string? body, out int inserted, out int modified, out string skippedReasons)
    {
        inserted = 0;
        modified = 0;
        skippedReasons = "";
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("inserted", out var insertedEl) || insertedEl.ValueKind != JsonValueKind.Number)
                return false;
            if (!root.TryGetProperty("modified", out var modifiedEl) || modifiedEl.ValueKind != JsonValueKind.Number)
                return false;
            inserted = insertedEl.GetInt32();
            modified = modifiedEl.GetInt32();
            if (root.TryGetProperty("skippedReasons", out var skippedEl) && skippedEl.ValueKind == JsonValueKind.Array && skippedEl.GetArrayLength() > 0)
                skippedReasons = skippedEl.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ExportResult Ok(string message) => new(ExportResultKind.Success, message);

    private static ExportResult Fail(string message) => new(ExportResultKind.Error, message);

    private static string Unclassified(ImportCallResult result)
    {
        var body = result.Body ?? "";
        body = body.Replace("\r", " ").Replace("\n", " ").Trim();
        if (body.Length > 180) body = body[..180];
        return body.Length == 0
            ? $"WowUtils did not classify every award ({result.StatusCode})."
            : $"WowUtils did not classify every award ({result.StatusCode}). {body}";
    }

    private static string? EffectiveId(ArchivedAward award) =>
        award.EffectiveFields.TryGetValue("id", out var v) ? v as string : null;
}
