using KARTCompanion.Matching;
using KARTCompanion.WowUtils;

namespace KARTCompanion.Simulations;

/// <summary>
/// Common interface for both sim sources (Raidbots, QE Live). Never throws — any failure
/// (expired report, network error, unexpected shape) is caught internally and returns null so
/// SyncEngine can skip just that one character instead of aborting the whole sync.
/// </summary>
public interface ISimReportFetcher
{
    /// <summary>Which DroptimizerSummary.Source value this fetcher handles ("raidbots" or "qelive").</summary>
    string Source { get; }

    Task<IReadOnlyList<GainCandidate>?> TryGetGainsAsync(DroptimizerSummary summary, CancellationToken ct = default);
}
