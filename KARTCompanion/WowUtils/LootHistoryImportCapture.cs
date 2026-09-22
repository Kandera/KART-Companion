using System.Text.Json;

namespace KARTCompanion.WowUtils;

public sealed record LootHistoryImportCapture(bool Captured, string PreviewUrl, string ConfirmUrl, string AuthHeaderName)
{
    public bool IsEnabled => Captured && PreviewUrl.Length > 0 && ConfirmUrl.Length > 0 && AuthHeaderName.Length > 0;

    public static LootHistoryImportCapture Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        return new LootHistoryImportCapture(
            root.GetProperty("captured").GetBoolean(),
            root.GetProperty("previewUrl").GetString() ?? "",
            root.GetProperty("confirmUrl").GetString() ?? "",
            root.GetProperty("authHeaderName").GetString() ?? "");
    }
}
