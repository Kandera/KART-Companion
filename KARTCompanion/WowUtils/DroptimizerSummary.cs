using System.Text.Json.Serialization;

namespace KARTCompanion.WowUtils;

/// <summary>
/// One entry from GET /v1/groups/{groupId}/droptimizers — a summary of an imported sim, not
/// the sim's actual per-item data (that has to be fetched separately from Raidbots/QE Live).
/// </summary>
public sealed class DroptimizerSummary
{
    [JsonPropertyName("characterId")]
    public string CharacterId { get; set; } = "";

    [JsonPropertyName("characterName")]
    public string CharacterName { get; set; } = "";

    [JsonPropertyName("characterClass")]
    public string CharacterClass { get; set; } = "";

    [JsonPropertyName("characterSpec")]
    public string? CharacterSpec { get; set; }

    [JsonPropertyName("profileKey")]
    public string? ProfileKey { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("baselineDps")]
    public double BaselineDps { get; set; }

    [JsonPropertyName("reportId")]
    public string ReportId { get; set; } = "";

    [JsonPropertyName("reportUrl")]
    public string ReportUrl { get; set; } = "";

    [JsonPropertyName("importedAt")]
    public DateTimeOffset ImportedAt { get; set; }
}

public sealed class DroptimizerList
{
    [JsonPropertyName("data")]
    public List<DroptimizerSummary> Data { get; set; } = new();
}
