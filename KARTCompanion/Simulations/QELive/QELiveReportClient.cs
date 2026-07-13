using KARTCompanion.Matching;
using KARTCompanion.WowUtils;

namespace KARTCompanion.Simulations.QELive;

/// <summary>
/// Fetches a QE Live healer upgrade report and extracts gain candidates from it.
/// GET https://questionablyepic.com/api/getUpgradeReport.php?reportID={reportCode} —
/// reverse-engineered from QE Live's own open-source frontend (github.com/Voulk/QuestionablyEpic),
/// not formally documented either, but confirmed live this session against a real report.
///
/// The response body is sometimes double-JSON-encoded (the whole payload is itself a JSON
/// string containing the real JSON object) — QE Live's own frontend defensively re-parses when
/// `typeof(data) === "string"`, and error responses (e.g. "Report not found") come back as a
/// plain object instead. This client handles both shapes.
///
/// Never throws: any failure returns null so SyncEngine skips just this one character.
/// </summary>
public sealed class QELiveReportClient : ISimReportFetcher
{
    private readonly HttpClient _http;

    public string Source => "qelive";

    public QELiveReportClient(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public async Task<IReadOnlyList<GainCandidate>?> TryGetGainsAsync(DroptimizerSummary summary, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://questionablyepic.com/api/getUpgradeReport.php?reportID={Uri.EscapeDataString(summary.ReportId)}";
            var rawBody = await _http.GetStringAsync(url, ct);

            var report = QELiveReportParser.Parse(rawBody);
            if (report is null || report.Results.Count == 0) return null;

            return QELiveGainExtractor.Extract(report, summary.ProfileKey, summary.ImportedAt);
        }
        catch
        {
            return null;
        }
    }
}
