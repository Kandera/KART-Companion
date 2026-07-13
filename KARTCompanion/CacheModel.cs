using KARTCompanion.Matching;

namespace KARTCompanion;

/// <summary>
/// The full payload synced into KART_WoWUtilsCache. Keys of Players are lowercase "name-realm",
/// matching WoWUtils' own characterId format — this is the contract the addon's Droptimizer.lua
/// (KART.DT.RebuildIndex) is built against. schemaVersion lets the addon detect/ignore output
/// from an incompatible future version of this companion instead of erroring on an unexpected
/// shape.
/// </summary>
public sealed class CacheModel
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset SyncedAt { get; init; }
    public string? GroupId { get; init; }
    public Dictionary<string, List<GainCandidate>> Players { get; init; } = new();
}
