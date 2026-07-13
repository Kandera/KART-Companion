using System.Net.Http.Json;
using KARTCompanion.Matching;
using KARTCompanion.WowUtils;

namespace KARTCompanion.Simulations.Raidbots;

/// <summary>
/// Fetches a Raidbots droptimizer report and extracts gain candidates from it.
/// GET https://www.raidbots.com/reports/{reportId}/data.json — confirmed working live this
/// session, but NOT an officially documented Raidbots endpoint (found via an archived
/// open-source API wrapper that used the same URL pattern). Treated as unstable: any failure
/// (404 for an expired report, unexpected JSON shape, network error) is caught here and
/// returns null so SyncEngine skips just this one character.
/// </summary>
public sealed class RaidbotsReportClient : ISimReportFetcher
{
    private readonly HttpClient _http;

    public string Source => "raidbots";

    public RaidbotsReportClient(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public async Task<IReadOnlyList<GainCandidate>?> TryGetGainsAsync(DroptimizerSummary summary, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://www.raidbots.com/reports/{summary.ReportId}/data.json";
            var report = await _http.GetFromJsonAsync<RaidbotsReport>(url, ct);
            if (report is null) return null;

            return RaidbotsGainExtractor.Extract(report, summary.ProfileKey, summary.ImportedAt);
        }
        catch
        {
            return null;
        }
    }
}
