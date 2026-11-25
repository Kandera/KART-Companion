using System.Net;
using KARTCompanion.Archive;
using KARTCompanion.Export;
using KARTCompanion.WowUtils;

namespace KARTCompanion.Tests;

public class WowUtilsExportServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public async Task Run_WhenUploadDisabled_DoesNotCallTransport()
    {
        var transport = new FakeTransport();
        var doc = Doc(Award("a"));
        var result = await WowUtilsExportService.Run(() => doc, _ => { }, null, transport, uploadEnabled: false, hasCredential: true, Now);

        Assert.Equal(0, transport.PreviewCalls);
        Assert.Equal(0, transport.ConfirmCalls);
        Assert.Equal(ExportResultKind.Error, result.Kind);
        Assert.Contains("not captured", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_ChunkAccepted_StampsReceiptsAndDoesNotConfirm()
    {
        var transport = new FakeTransport(previewBody: """{"inserted":1,"modified":0,"skippedReasons":[]}""");
        ArchiveDocument? saved = null;
        var doc = Doc(Award("a"));

        var result = await WowUtilsExportService.Run(() => doc, d => saved = d, null, transport, true, true, Now);

        Assert.Equal(ExportResultKind.Success, result.Kind);
        Assert.Equal(1, transport.PreviewCalls);
        Assert.Equal(0, transport.ConfirmCalls);
        Assert.Contains("\"chunkIndex\":0", transport.LastPreviewBody);
        Assert.Contains("\"entries\":[", transport.LastPreviewBody);
        Assert.Equal(Now, saved!.Awards.Single().WowUtilsImportedAt);
    }

    [Fact]
    public async Task Run_SkippedReasons_DoesNotReceipt()
    {
        var doc = Doc(Award("a"));
        var transport = new FakeTransport(previewBody: """{"inserted":0,"modified":0,"skippedReasons":["row 1 invalid"]}""");

        var result = await WowUtilsExportService.Run(() => doc, _ => { }, null, transport, true, true, Now);

        Assert.Equal(ExportResultKind.Error, result.Kind);
        Assert.Contains("skipped", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, transport.ConfirmCalls);
        Assert.Null(doc.Awards.Single().WowUtilsImportedAt);
    }

    [Fact]
    public async Task Run_NonJsonBody_DoesNotReceipt()
    {
        var doc = Doc(Award("a"));
        var transport = new FakeTransport(previewBody: "<!DOCTYPE html>");

        var result = await WowUtilsExportService.Run(() => doc, _ => { }, null, transport, true, true, Now);

        Assert.Equal(ExportResultKind.Error, result.Kind);
        Assert.Contains("<!DOCTYPE html>", result.Message);
        Assert.Null(doc.Awards.Single().WowUtilsImportedAt);
    }

    [Fact]
    public async Task Run_Non200_DoesNotReceipt()
    {
        var doc = Doc(Award("a"));
        var transport = new FakeTransport(previewStatus: 404, previewBody: "<!DOCTYPE html>");

        var result = await WowUtilsExportService.Run(() => doc, _ => { }, null, transport, true, true, Now);

        Assert.Equal(ExportResultKind.Error, result.Kind);
        Assert.Contains("404", result.Message);
        Assert.Null(doc.Awards.Single().WowUtilsImportedAt);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Run_401Or403OnPreview_DoesNotConfirm(int status)
    {
        var doc = Doc(Award("a"));
        var transport = new FakeTransport(previewStatus: status);

        var result = await WowUtilsExportService.Run(() => doc, _ => { }, null, transport, true, true, Now);

        Assert.Equal(ExportResultKind.Error, result.Kind);
        Assert.Equal(0, transport.ConfirmCalls);
        Assert.Null(doc.Awards.Single().WowUtilsImportedAt);
        Assert.Contains("login", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_ExcludedReason_IsNotSentOrReceipted()
    {
        var keep = Award("keep", reason: "BiS");
        var bonus = Award("bonus", reason: "Bonus Loot");
        var doc = Doc(keep, bonus);
        var transport = new FakeTransport(previewBody: """{"inserted":1,"modified":0,"skippedReasons":[]}""");

        var result = await WowUtilsExportService.Run(
            () => doc, _ => { }, null, transport, true, true, Now,
            excludedReasons: new HashSet<string> { "Bonus Loot" });

        Assert.Equal(ExportResultKind.Success, result.Kind);
        Assert.Contains("keep", transport.LastPreviewBody);
        Assert.DoesNotContain("bonus", transport.LastPreviewBody);
        Assert.Equal(Now, keep.WowUtilsImportedAt);
        Assert.Null(bonus.WowUtilsImportedAt);
        Assert.Contains("1 left out by reason", result.Message);
    }

    [Fact]
    public async Task Run_RefreshRunsBeforeLoad()
    {
        var order = new List<string>();
        var doc = Doc(Award("a"));
        var transport = new FakeTransport(previewBody: """{"inserted":0,"modified":1,"skippedReasons":[]}""");

        var result = await WowUtilsExportService.Run(
            () => { order.Add("load"); return doc; },
            _ => { },
            () => order.Add("refresh"),
            transport, true, true, Now);

        Assert.Equal(ExportResultKind.Success, result.Kind);
        Assert.Equal(new[] { "refresh", "load" }, order);
    }

    [Fact]
    public void Importer_PutsCredentialOnlyOnItsOwnRequest()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHandler(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"results":[]}""") }; });
        using var http = new HttpClient(handler);
        var capture = new LootHistoryImportCapture(true, "https://wowutils.example/preview", "https://wowutils.example/confirm", "Cookie");
        var importer = new LootHistoryImporter(http, capture, "session=secret");

        importer.PreviewAsync("[]", CancellationToken.None).GetAwaiter().GetResult();

        Assert.Null(http.DefaultRequestHeaders.Authorization);
        Assert.Equal("session=secret", captured!.Headers.GetValues("Cookie").Single());
        Assert.Equal("https://wowutils.example/preview", captured.RequestUri!.ToString());
        Assert.DoesNotContain("api.wowutils.com", captured.RequestUri.ToString());
    }

    [Fact]
    public void CaptureFixture_PointsAtTheLootHistoryImport()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "wowutils-loot-import.capture.json");
        var capture = LootHistoryImportCapture.Load(path);

        Assert.True(capture.IsEnabled);
        Assert.Contains("/loot-history/import", capture.PreviewUrl, StringComparison.Ordinal);
        Assert.Equal(capture.PreviewUrl, capture.ConfirmUrl);
        Assert.Equal("Cookie", capture.AuthHeaderName);
    }

    private static ArchiveDocument Doc(params ArchivedAward[] awards) => new() { Awards = awards.ToList() };

    private static ArchivedAward Award(string id, DateTimeOffset? uncertain = null, string? reason = null) =>
        new()
        {
            Fields = reason == null
                ? new Dictionary<string, object?> { ["id"] = id }
                : new Dictionary<string, object?> { ["id"] = id, ["reason"] = reason },
            ExportedByCompanionAt = null,
            WowUtilsConfirmUncertainAt = uncertain,
        };

    private static ArchiveDocument Snapshot(ArchiveDocument doc)
    {
        var award = doc.Awards.Single();
        return Doc(new ArchivedAward
        {
            Fields = new Dictionary<string, object?>(award.Fields),
            WowUtilsImportedAt = award.WowUtilsImportedAt,
            WowUtilsConfirmUncertainAt = award.WowUtilsConfirmUncertainAt,
        });
    }

    private static string Results(params (string Id, string Status)[] rows)
    {
        var parts = rows.Select(r => $$"""{"id":"{{r.Id}}","status":"{{r.Status}}"}""");
        return $$"""{"results":[{{string.Join(",", parts)}}]}""";
    }

    private sealed class FakeTransport : ILootHistoryImportTransport
    {
        private readonly int _previewStatus;
        private readonly string? _previewBody;
        private readonly int _confirmStatus;
        private readonly string? _confirmBody;
        private readonly Action<string>? _onConfirm;

        public FakeTransport(
            string? previewBody = null,
            string? confirmBody = null,
            int previewStatus = 200,
            int confirmStatus = 200,
            Action<string>? onConfirm = null)
        {
            _previewBody = previewBody;
            _confirmBody = confirmBody;
            _previewStatus = previewStatus;
            _confirmStatus = confirmStatus;
            _onConfirm = onConfirm;
        }

        public int PreviewCalls { get; private set; }
        public int ConfirmCalls { get; private set; }
        public string? LastPreviewBody { get; private set; }

        public Task<ImportCallResult> PreviewAsync(string json, CancellationToken cancellationToken)
        {
            PreviewCalls++;
            LastPreviewBody = json;
            return Task.FromResult(new ImportCallResult(_previewStatus, _previewBody));
        }

        public Task<ImportCallResult> ConfirmAsync(string json, CancellationToken cancellationToken)
        {
            ConfirmCalls++;
            _onConfirm?.Invoke(json);
            return Task.FromResult(new ImportCallResult(_confirmStatus, _confirmBody));
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
