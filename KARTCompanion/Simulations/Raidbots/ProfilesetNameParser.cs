namespace KARTCompanion.Simulations.Raidbots;

/// <summary>
/// Parses a Raidbots droptimizer profileset name, e.g.
/// "1307/2735/raid-mythic/249339/279/0/trinket1///" — a slash-delimited string confirmed live
/// this session (10 tokens in the sample, last few trailing ones can be empty). Token order:
/// bonusId1/bonusId2/source/itemId/ilvl/gemBonusId/slot/.../.../...
/// </summary>
public static class ProfilesetNameParser
{
    public sealed record ParsedName(int ItemId, int? Ilvl, string? Slot, string? Source);

    public static ParsedName? Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var tokens = name.Split('/');
        if (tokens.Length < 7) return null;

        if (!int.TryParse(tokens[3], out var itemId)) return null;

        int? ilvl = int.TryParse(tokens[4], out var ilvlValue) ? ilvlValue : null;
        var slot = string.IsNullOrEmpty(tokens[6]) ? null : tokens[6];
        var source = string.IsNullOrEmpty(tokens[2]) ? null : tokens[2];

        return new ParsedName(itemId, ilvl, slot, source);
    }
}
