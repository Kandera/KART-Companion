namespace KARTCompanion.WowUtils;

/// <summary>
/// Only the droptimizer endpoint is needed for v1. Kept as its own interface (rather than a
/// method directly on WowUtilsClient) so future endpoints WowUtils exposes (wishlists, roster,
/// eventually loot history) can be added as sibling interfaces sharing the same client's
/// HTTP/auth plumbing, without reshaping SyncEngine.
/// </summary>
public interface IWowUtilsFetcher
{
    Task<IReadOnlyList<DroptimizerSummary>> GetDroptimizersAsync(string groupId, CancellationToken ct = default);
}
