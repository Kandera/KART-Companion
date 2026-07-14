using System.Net;
using KARTCompanion.WowUtils;

namespace KARTCompanion.Tests;

public class WowUtilsClientTests
{
    private const string DiscoveryJson = """{"apiVersion":"1","group":{"groupId":"g1","name":"Test Group"}}""";

    [Fact]
    public async Task GetDiscoveryAsync_DoesNotLeakAuthHeaderOntoSharedHttpClient()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(DiscoveryJson));
        using var sharedClient = new HttpClient(handler);
        var wowUtils = new WowUtilsClient(sharedClient, "super-secret-group-key");

        await wowUtils.GetDiscoveryAsync();

        // This client instance is also handed to RaidbotsReportClient/QELiveReportClient in
        // Program.cs — if WowUtilsClient sets DefaultRequestHeaders here, the group key would be
        // sent to those third-party hosts on every request too.
        Assert.Null(sharedClient.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task GetDiscoveryAsync_DoesNotChangeSharedHttpClientBaseAddress()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(DiscoveryJson));
        using var sharedClient = new HttpClient(handler);
        var wowUtils = new WowUtilsClient(sharedClient, "super-secret-group-key");

        await wowUtils.GetDiscoveryAsync();

        Assert.Null(sharedClient.BaseAddress);
    }

    [Fact]
    public async Task GetDiscoveryAsync_StillSendsBearerTokenOnItsOwnRequest()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req;
            return JsonResponse(DiscoveryJson);
        });
        using var sharedClient = new HttpClient(handler);
        var wowUtils = new WowUtilsClient(sharedClient, "super-secret-group-key");

        await wowUtils.GetDiscoveryAsync();

        Assert.NotNull(captured!.Headers.Authorization);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("super-secret-group-key", captured.Headers.Authorization!.Parameter);
        Assert.StartsWith("https://api.wowutils.com", captured.RequestUri!.ToString());
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
