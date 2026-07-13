using System.Text.Json.Serialization;

namespace KARTCompanion.Simulations.Raidbots;

/// <summary>
/// Minimal shape of https://www.raidbots.com/reports/{reportId}/data.json — only the fields
/// needed to compute item gains are modeled; the real file is ~500KB of full SimC output.
/// Confirmed live against a real report this session (id f2u18PPKqWEQJw9wu963WJ) — see project
/// memory project_droptimizer_gain_feature for the full writeup. Unknown JSON properties are
/// silently ignored by System.Text.Json by default, which is what we want here.
/// </summary>
public sealed class RaidbotsReport
{
    [JsonPropertyName("sim")]
    public RbSim Sim { get; set; } = new();
}

public sealed class RbSim
{
    [JsonPropertyName("players")]
    public List<RbPlayer> Players { get; set; } = new();

    [JsonPropertyName("profilesets")]
    public RbProfilesets Profilesets { get; set; } = new();
}

public sealed class RbPlayer
{
    [JsonPropertyName("collected_data")]
    public RbCollectedData CollectedData { get; set; } = new();
}

public sealed class RbCollectedData
{
    [JsonPropertyName("dps")]
    public RbMetric? Dps { get; set; }

    [JsonPropertyName("hps")]
    public RbMetric? Hps { get; set; }
}

public sealed class RbMetric
{
    [JsonPropertyName("mean")]
    public double Mean { get; set; }
}

public sealed class RbProfilesets
{
    [JsonPropertyName("metric")]
    public string? Metric { get; set; }

    [JsonPropertyName("results")]
    public List<RbProfilesetResult> Results { get; set; } = new();
}

public sealed class RbProfilesetResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("mean")]
    public double Mean { get; set; }
}
