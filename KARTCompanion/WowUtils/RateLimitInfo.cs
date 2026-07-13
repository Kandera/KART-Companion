namespace KARTCompanion.WowUtils;

/// <summary>
/// Parsed from the X-Ratelimit-* response headers WoWUtils sends on every response (confirmed
/// live against the real API — see project memory). Surfaced in the tray tooltip/Settings
/// dialog so the officer sees budget usage before hitting a 429, not only after.
/// </summary>
public sealed record RateLimitInfo(int Limit, int Remaining, DateTimeOffset ResetAt);
