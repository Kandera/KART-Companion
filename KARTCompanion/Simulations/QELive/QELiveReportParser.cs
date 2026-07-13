using System.Text.Json;

namespace KARTCompanion.Simulations.QELive;

/// <summary>
/// Parses a raw QE Live API response body into a QELiveReport. Split out from
/// QELiveReportClient so this parsing logic (in particular the double-JSON-decode handling) is
/// unit-testable without mocking HTTP.
/// </summary>
public static class QELiveReportParser
{
    public static QELiveReport? Parse(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var jsonText = doc.RootElement.ValueKind == JsonValueKind.String
            ? doc.RootElement.GetString()
            : rawBody;

        return string.IsNullOrEmpty(jsonText) ? null : JsonSerializer.Deserialize<QELiveReport>(jsonText);
    }
}
