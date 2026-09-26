using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Export;

/// <summary>
/// Emits the same RCLootCouncil-compatible JSON the addon's own LH.BuildRCLootCouncilJSON produces,
/// field for field, so the maintainer can paste either one into a tool built to read that format.
///
/// <c>id</c> is the award id stored on the row, empty when the row has none. <c>itemID</c> is parsed
/// from the item link when present, else zero. <c>instance</c> is the instance name from the row.
/// <c>subType</c> and <c>equipLoc</c> still come, on the addon's side, from C_Item.GetItemInfoInstant;
/// the Companion has no item database and emits both empty here.
///
/// <c>Write</c> always sorts newest-first and ignores the order it was given awards in. Every real
/// caller wants exactly this — it is what LH.BuildRCLootCouncilJSON's own default (no explicit list)
/// produces, and the export dialog has no other order to offer — so the writer owns it rather than
/// pushing the same sort onto every call site.
///
/// The date/time fields are rendered in a caller-supplied time zone, defaulting to the machine's
/// local zone (matching the addon, which uses Lua's date() and is therefore always local to whatever
/// machine WoW is running on). The override exists so a test can pin the zone the golden fixture was
/// actually captured in, rather than depending on whichever machine happens to run the test.
/// </summary>
public static class RcLootCouncilJsonWriter
{
    // |H(item:...)|h — the item string exactly as the client wrote it, matched by delimiter so no
    // assumption is made about which separators a given client build uses inside it.
    private static readonly Regex ItemStringPattern = new(@"\|H(item:[^|]+)\|h", RegexOptions.Compiled);
    private static readonly Regex ItemIdPattern = new(@"item:(\d+)", RegexOptions.Compiled);

    // The display name inside a hyperlink's brackets: |c...|Hitem:...|h[Name]|h|r.
    private static readonly Regex ItemNamePattern = new(@"\[(.*?)\]", RegexOptions.Compiled);

    public static string Write(IEnumerable<LootHistoryEntry> awards, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;

        // Newest first, mirroring LH.GetFilteredEntries. Awards sharing a second keep the order the
        // saved-variables file had; the addon leaves that order to Lua's table.sort, which does not
        // specify it, so the Companion pins it deliberately instead of imitating it.
        var list = awards.OrderByDescending(e => e.Time).ToList();

        var objects = new List<string>();
        for (var i = 0; i < list.Count; i++)
        {
            objects.Add(WriteObject(list[i], zone));
        }
        return "[" + string.Join(",", objects) + "]";
    }

    private static string WriteObject(LootHistoryEntry e, TimeZoneInfo timeZone)
    {
        var time = e.Time;
        var localTime = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(time), timeZone);

        var fields = new[]
        {
            JsonString("player", e.Winner ?? ""),
            JsonString("date", StoredOrFormatted(e, "dateStr", localTime.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))),
            JsonString("time", StoredOrFormatted(e, "timeStr", localTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture))),
            JsonString("id", IdForExport(e.Id)),
            JsonNumber("itemID", ItemIdFromLink(e.Item)),
            JsonString("itemString", TrimTrailingColons(GetItemString(e.Item))),
            JsonString("response", e.Reason ?? ""),
            JsonNumber("votes", Votes(e)),
            JsonString("class", e.Class ?? ""),
            JsonString("instance", InstanceText(e)),
            JsonString("boss", e.Boss ?? ""),
            JsonString("gear1", Stored(e, "gear1")),
            JsonString("gear2", Stored(e, "gear2")),
            JsonString("responseID", ResponseId(e)),
            IsAwardReason(e) ? "\"isAwardReason\":true" : "\"isAwardReason\":false",
            JsonString("rollType", "normal"),
            JsonString("subType", ""),
            JsonString("equipLoc", ""),
            JsonString("note", Stored(e, "note")),
            JsonString("owner", OwnerForExport(e)),
            JsonString("itemName", GetItemNameFromLink(e.Item)),
            JsonNumber("servertime", ServerTimeNumber(e, time)),
        };
        return "{" + string.Join(",", fields) + "}";
    }

    private static long ItemIdFromLink(string? link)
    {
        if (string.IsNullOrEmpty(link)) return 0;
        var m = ItemIdPattern.Match(link);
        return m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : 0;
    }

    private static string Stored(LootHistoryEntry e, string key) =>
        e.Fields.TryGetValue(key, out var v) && v is string s ? s : "";

    // WowUtils rejects an owner that is not Name-Realm. RC's own history uses that form.
    // Rows written from the trade, before history was taken from RC again, stored no owner.
    // RC's fallback when it has no looter is the winner, so the export uses that same fallback.
    private static string OwnerForExport(LootHistoryEntry e)
    {
        var owner = Stored(e, "owner");
        if (owner.Contains('-')) return owner;
        var winner = e.Winner ?? "";
        if (!winner.Contains('-')) return owner;
        if (owner.Length == 0) return winner;
        var winnerName = winner[..winner.IndexOf('-')];
        return string.Equals(owner, winnerName, StringComparison.OrdinalIgnoreCase) ? winner : owner;
    }

    private static string StoredOrFormatted(LootHistoryEntry e, string key, string fallback)
    {
        var stored = Stored(e, key);
        return stored.Length > 0 ? stored : fallback;
    }

    private static string InstanceText(LootHistoryEntry e)
    {
        var instance = e.Instance;
        if (!string.IsNullOrEmpty(instance)) return instance;
        return Stored(e, "instanceLabel");
    }

    private static string ResponseId(LootHistoryEntry e)
    {
        if (!e.Fields.TryGetValue("responseID", out var v) || v == null) return "0";
        if (v is string s) return s;
        if (v is double d) return ((long)d).ToString(CultureInfo.InvariantCulture);
        return v.ToString() ?? "0";
    }

    private static long Votes(LootHistoryEntry e) =>
        e.Fields.TryGetValue("votes", out var v) && v is double d ? (long)d : 0;

    private static bool IsAwardReason(LootHistoryEntry e) =>
        e.Fields.TryGetValue("isAwardReason", out var v) && v is true;

    // KART minted ids as "<unix>-<3 hex digits>" ("%d-%03x"). WowUtils wants
    // "<servertime>-<seq>" with a decimal sequence and no leading zeros.
    // An id that is already in that form, including RC's own, is left alone.
    private static string IdForExport(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var dash = id.IndexOf('-');
        if (dash <= 0 || dash != id.LastIndexOf('-')) return id;
        var prefix = id[..dash];
        var suffix = id[(dash + 1)..];
        if (prefix.Length == 0 || suffix.Length == 0) return id;
        if (!prefix.All(char.IsAsciiDigit) || !suffix.All(Uri.IsHexDigit)) return id;
        if (suffix.All(char.IsAsciiDigit) && (suffix.Length == 1 || suffix[0] != '0')) return id;
        if (!long.TryParse(suffix, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var seq)) return id;
        return prefix + "-" + seq.ToString(CultureInfo.InvariantCulture);
    }

    private static long ServerTimeNumber(LootHistoryEntry e, long time)
    {
        var text = ServerTime(e, time);
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : time;
    }

    private static string ServerTime(LootHistoryEntry e, long time)
    {
        var id = e.Id;
        if (!string.IsNullOrEmpty(id))
        {
            var dash = id.IndexOf('-');
            var prefix = dash > 0 ? id[..dash] : id;
            if (long.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return prefix;
        }
        return time.ToString(CultureInfo.InvariantCulture);
    }

    private static string TrimTrailingColons(string itemString) => itemString.TrimEnd(':');

    private static string GetItemString(string? link)
    {
        if (string.IsNullOrEmpty(link)) return "";
        var m = ItemStringPattern.Match(link);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string GetItemNameFromLink(string? link)
    {
        if (string.IsNullOrEmpty(link)) return "";
        var m = ItemNamePattern.Match(link);
        return m.Success ? m.Groups[1].Value : link;
    }

    private static string JsonString(string key, string value) =>
        $"\"{key}\":\"{JsonEscape(value)}\"";

    private static string JsonNumber(string key, long value) =>
        $"\"{key}\":{value.ToString(CultureInfo.InvariantCulture)}";

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c <= '\u001f') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
