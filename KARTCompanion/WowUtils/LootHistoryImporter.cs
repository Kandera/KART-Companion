using System.Text;

namespace KARTCompanion.WowUtils;

public sealed record ImportCallResult(int StatusCode, string? Body);

public interface ILootHistoryImportTransport
{
    Task<ImportCallResult> PreviewAsync(string json, CancellationToken cancellationToken);
    Task<ImportCallResult> ConfirmAsync(string json, CancellationToken cancellationToken);
}

public sealed class LootHistoryImporter : ILootHistoryImportTransport
{
    private readonly HttpClient _http;
    private readonly LootHistoryImportCapture _capture;
    private readonly string _credential;

    public LootHistoryImporter(HttpClient http, LootHistoryImportCapture capture, string credential)
    {
        _http = http;
        _capture = capture;
        _credential = credential;
    }

    public Task<ImportCallResult> PreviewAsync(string json, CancellationToken cancellationToken) =>
        PostAsync(_capture.PreviewUrl, json, cancellationToken);

    public Task<ImportCallResult> ConfirmAsync(string json, CancellationToken cancellationToken) =>
        PostAsync(_capture.ConfirmUrl, json, cancellationToken);

    private async Task<ImportCallResult> PostAsync(string url, string json, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation(_capture.AuthHeaderName, _credential);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ImportCallResult((int)response.StatusCode, body);
    }
}
