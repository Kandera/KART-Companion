using System.Text.Json.Serialization;

namespace KARTCompanion.Simulations.QELive;

/// <summary>
/// Shape of a QE Live upgrade report, confirmed live this session against a real report (id
/// efisrltzytyb — see project memory project_droptimizer_gain_feature). Note the wire field is
/// "playername", not "charName" (that's only the client-side JS variable name in QE Live's own
/// frontend source, github.com/Voulk/QuestionablyEpic).
/// </summary>
public sealed class QELiveReport
{
    [JsonPropertyName("playername")]
    public string PlayerName { get; set; } = "";

    [JsonPropertyName("realm")]
    public string Realm { get; set; } = "";

    [JsonPropertyName("region")]
    public string Region { get; set; } = "";

    [JsonPropertyName("spec")]
    public string? Spec { get; set; }

    [JsonPropertyName("results")]
    public List<QELiveResult> Results { get; set; } = new();
}

public sealed class QELiveResult
{
    [JsonPropertyName("item")]
    public int Item { get; set; }

    [JsonPropertyName("level")]
    public int? Level { get; set; }

    [JsonPropertyName("dropLoc")]
    public string? DropLoc { get; set; }

    // "dropDifficulty" deliberately not modeled: confirmed from the real fixture that QE Live
    // returns it as either a number (5/6/7 for Raid/Dungeon) or an empty string (Crafted/Delves)
    // for the same field, and nothing here actually needs it.

    [JsonPropertyName("rawDiff")]
    public double RawDiff { get; set; }

    /// <summary>Already a percentage (e.g. 1.291 means +1.291%), not a fraction — confirmed
    /// live. No baseline math needed on our side, unlike Raidbots.</summary>
    [JsonPropertyName("percDiff")]
    public double PercDiff { get; set; }
}
