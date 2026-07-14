using System.Net;
using KARTCompanion.Simulations.Raidbots;
using KARTCompanion.WowUtils;

namespace KARTCompanion.Tests;

public class RaidbotsReportClientTests
{
    [Fact]
    public async Task TryGetGainsAsync_EscapesTheReportIdInTheRequestUrl()
    {
        // reportId comes from the WoWUtils API response, not the WoW addon directly, but it's
        // still external/untrusted data — QELiveReportClient already escapes it, this closes the
        // same gap here for consistency and defense-in-depth against malformed API data.
        Uri? captured = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        var client = new RaidbotsReportClient(http);
        var summary = new DroptimizerSummary { ReportId = "abc/def?&weird id", ProfileKey = "p1", ImportedAt = DateTimeOffset.UtcNow };

        await client.TryGetGainsAsync(summary);

        // Uri.ToString() deliberately unescapes characters like space back for readability —
        // AbsoluteUri is the actual escaped form that goes out on the wire.
        Assert.NotNull(captured);
        Assert.DoesNotContain(" ", captured!.AbsoluteUri);
        Assert.StartsWith("https://www.raidbots.com/reports/abc%2Fdef%3F%26weird%20id/data.json", captured.AbsoluteUri);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
