using KARTCompanion.Config;
using KARTCompanion.Matching;
using KARTCompanion.SavedVariables;
using KARTCompanion.WowUtils;

namespace KARTCompanion;

public sealed record SyncResult(bool Success, int PlayerCount, int SkippedCharacters, string? ErrorMessage);

/// <summary>
/// Orchestrates one full sync: fetch droptimizer summaries from WoWUtils, fetch+parse each
/// character's sim report from whichever source it came from (Raidbots/QE Live), merge into a
/// CacheModel, and write it into the addon's SavedVariables file. A single character's sim
/// fetch failing (expired report, unexpected shape, etc.) only skips that character — it never
/// aborts the whole sync.
/// </summary>
public sealed class SyncEngine
{
    private readonly IWowUtilsFetcher _wowUtils;
    private readonly IReadOnlyDictionary<string, Simulations.ISimReportFetcher> _simFetchersBySource;
    private readonly Func<CompanionConfig> _getConfig;
    private readonly Action<CompanionConfig> _saveConfig;

    public event Action? SyncStarted;
    public event Action<SyncResult>? SyncCompleted;

    public SyncEngine(
        IWowUtilsFetcher wowUtils,
        IEnumerable<Simulations.ISimReportFetcher> simFetchers,
        Func<CompanionConfig> getConfig,
        Action<CompanionConfig> saveConfig)
    {
        _wowUtils = wowUtils;
        _simFetchersBySource = simFetchers.ToDictionary(f => f.Source, StringComparer.OrdinalIgnoreCase);
        _getConfig = getConfig;
        _saveConfig = saveConfig;
    }

    public async Task<SyncResult> RunOnceAsync(string groupId, CancellationToken ct = default)
    {
        SyncStarted?.Invoke();

        SyncResult result;
        try
        {
            result = await RunAsync(groupId, ct);
        }
        catch (Exception ex)
        {
            result = new SyncResult(false, 0, 0, ex.Message);
        }

        SyncCompleted?.Invoke(result);
        return result;
    }

    private async Task<SyncResult> RunAsync(string groupId, CancellationToken ct)
    {
        var summaries = await _wowUtils.GetDroptimizersAsync(groupId, ct);

        var players = new Dictionary<string, List<GainCandidate>>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;

        foreach (var summary in summaries)
        {
            if (summary.Source is null || !_simFetchersBySource.TryGetValue(summary.Source, out var fetcher))
            {
                skipped++;
                continue;
            }

            var gains = await fetcher.TryGetGainsAsync(summary, ct);
            if (gains is null)
            {
                skipped++;
                continue;
            }

            var key = summary.CharacterId.ToLowerInvariant();
            if (!players.TryGetValue(key, out var list))
            {
                list = new List<GainCandidate>();
                players[key] = list;
            }
            list.AddRange(gains);
        }

        var model = new CacheModel
        {
            SyncedAt = DateTimeOffset.UtcNow,
            GroupId = groupId,
            Players = players,
        };

        var config = _getConfig();
        if (string.IsNullOrWhiteSpace(config.SavedVariablesFilePath))
            return new SyncResult(false, 0, skipped, "No SavedVariables file path configured.");

        var blockText = LuaTableWriter.WriteAssignment(model);
        SavedVariablesBlockWriter.WriteBlock(config.SavedVariablesFilePath, blockText);

        config.LastSyncUtc = model.SyncedAt;
        _saveConfig(config);

        return new SyncResult(true, players.Count, skipped, null);
    }
}
