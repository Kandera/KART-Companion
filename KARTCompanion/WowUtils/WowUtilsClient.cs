using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;

namespace KARTCompanion.WowUtils;

/// <summary>
/// Thin wrapper around the WoWUtils REST API (https://api.wowutils.com). Auth header confirmed
/// live against the real API: "Authorization: Bearer {groupKey}" — the OpenAPI spec doesn't
/// declare a securitySchemes block, so this couldn't be read from docs, only tested directly.
/// </summary>
public sealed class WowUtilsClient : IWowUtilsFetcher
{
    private const string BaseUrl = "https://api.wowutils.com";

    private readonly HttpClient _http;
    private readonly string _groupKey;

    public RateLimitInfo? LastRateLimit { get; private set; }

    // httpClient is shared with the Raidbots/QE Live sim fetchers (see Program.cs), so this must
    // never touch its DefaultRequestHeaders or BaseAddress — those apply to every request sent
    // through the shared instance, which would leak the group key to those third-party hosts.
    public WowUtilsClient(HttpClient httpClient, string groupKey)
    {
        _http = httpClient;
        _groupKey = groupKey;
    }

    public async Task<DiscoveryResponse> GetDiscoveryAsync(CancellationToken ct = default)
    {
        return await SendAsync<DiscoveryResponse>(HttpMethod.Get, "/v1", ct);
    }

    public async Task<IReadOnlyList<DroptimizerSummary>> GetDroptimizersAsync(string groupId, CancellationToken ct = default)
    {
        var list = await SendAsync<DroptimizerList>(HttpMethod.Get, $"/v1/groups/{groupId}/droptimizers", ct);
        return list.Data;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(BaseUrl + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _groupKey);
        using var response = await _http.SendAsync(request, ct);

        CaptureRateLimit(response);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new WowUtilsException(
                $"WoWUtils API request failed: {(int)response.StatusCode} {response.ReasonPhrase} — {body}",
                (int)response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        if (result is null)
            throw new WowUtilsException("WoWUtils API returned an empty/unparseable response body.");
        return result;
    }

    private void CaptureRateLimit(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Ratelimit-Limit", out var limitValues) ||
            !response.Headers.TryGetValues("X-Ratelimit-Remaining", out var remainingValues) ||
            !response.Headers.TryGetValues("X-Ratelimit-Reset", out var resetValues))
        {
            return;
        }

        if (int.TryParse(limitValues.FirstOrDefault(), out var limit) &&
            int.TryParse(remainingValues.FirstOrDefault(), out var remaining) &&
            DateTimeOffset.TryParse(resetValues.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var resetAt))
        {
            LastRateLimit = new RateLimitInfo(limit, remaining, resetAt);
        }
    }
}
