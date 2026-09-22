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
        var message = await WowUtilsExportService.Run(() => doc, _ => { }, null, transport, uploadEnabled: false, hasCredential: true, Now);

        Assert.Equal(0, transport.PreviewCalls);
        Assert.Equal(0, transport.ConfirmCalls);
        Assert.Contains("not captured", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_AllDuplicate_SavesReceiptsAndDoesNotConfirm()
    {
        var transport = new FakeTransport(previewBody: Results(("a", "duplicate")));
        ArchiveDocument? saved = null;
        var doc = Doc(Award("a"));

        await WowUtilsExportService.Run(() => doc, d => saved = d, null, transport, true, true, Now);

        Assert.Equal(1, transport.PreviewCalls);
        Assert.Equal(0, transport.ConfirmCalls);
        Assert.Equal(Now, saved!.Awards.Single().WowUtilsImportedAt);
    }

    [Fact]
    public async Task Run_AllNew_MarksUncertainBeforeConfirm_ThenReceiptsOnAccepted()
    {
        var doc = Doc(Award("a"));
        ArchiveDocument? savedBeforeConfirm = null;
        var transport = new FakeTransport(
            previewBody: Results(("a", "new")),
            confirmBody: """{"accepted":["a"]}""",
            onConfirm: _ => savedBeforeConfirm = Snapshot(doc));

        await WowUtilsExportService.Run(() => doc, d => { }, null, transport, true, true, Now);

        Assert.NotNull(savedBeforeConfirm!.Awards.Single().WowUtilsConfirmUncertainAt);
        Assert.Null(savedBeforeConfirm.Awards.Single().WowUtilsImportedAt);
        Assert.Equal(Now, doc.Awards.Single().WowUtilsImportedAt);
        Assert.Null(doc.Awards.Single().WowUtilsConfirmUncertainAt);
    }

    [Fact]
    public async Task Run_ConfirmBodyDoesNotNameIds_LeavesUncertainAndDoesNotReceipt()
    {
        var doc = Doc(Award("a"));
        var transport = new FakeTransport(previewBody: Results(("a", "new")), confirmBody: """{"ok":true}""");

        await WowUtilsExportService.Run(() => doc, _ => { }, null, transport, true, true, Now);

        Assert.Equal(1, transport.ConfirmCalls);
        Assert.Null(doc.Awards.Single().WowUtilsImportedAt);
        Assert.Equal(Now, doc.Awards.Single().WowUtilsConfirmUncertainAt);
    }

    [Fact]
    public async Task Run_LaterPreviewCallsUncertainIdNew_DoesNotConfirm()
    {
        var doc = Doc(Award("a", uncertain: Now));
        var transport = new FakeTransport(previewBody: Results(("a", "new")));

        var message = await WowUtilsExportService.Run(() => doc, _ => { }, null, transport, true, true, Now);

        Assert.Equal(0, transport.ConfirmCalls);
        Assert.Contains("came back as new", message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Run_401Or403OnPreview_DoesNotConfirm(int status)
    {
        var doc = Doc(Award("a"));
        var transport = new FakeTransport(previewStatus: status);

        var message = await WowUtilsExportService.Run(() => doc, _ => { }, null, transport, true, true, Now);

        Assert.Equal(0, transport.ConfirmCalls);
        Assert.Null(doc.Awards.Single().WowUtilsImportedAt);
        Assert.Contains("login", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_RefreshRunsBeforeLoad()
    {
        var order = new List<string>();
        var doc = Doc(Award("a"));
        var transport = new FakeTransport(previewBody: Results(("a", "duplicate")));

        await WowUtilsExportService.Run(
            () => { order.Add("load"); return doc; },
            _ => { },
            () => order.Add("refresh"),
            transport, true, true, Now);

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
    public void CaptureFixture_IsNotEnabled()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "wowutils-loot-import.capture.json");
        var capture = LootHistoryImportCapture.Load(path);

        Assert.False(capture.IsEnabled);
    }

    private static ArchiveDocument Doc(params ArchivedAward[] awards) => new() { Awards = awards.ToList() };

    private static ArchivedAward Award(string id, DateTimeOffset? uncertain = null) =>
        new()
        {
            Fields = new Dictionary<string, object?> { ["id"] = id },
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

        public Task<ImportCallResult> PreviewAsync(string json, CancellationToken cancellationToken)
        {
            PreviewCalls++;
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
