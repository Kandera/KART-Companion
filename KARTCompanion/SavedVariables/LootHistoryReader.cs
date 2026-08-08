using System.Globalization;
using System.Text;

namespace KARTCompanion.SavedVariables;

/// <summary>
/// Reads the KART_LootHistory block out of a WoW saved-variables file. Deliberately narrow: this is
/// not a Lua parser, it understands exactly the shape WoW's serializer emits.
///
/// The block boundary is the one SavedVariablesBlockWriter already depends on from the writing side:
/// each declared variable is its own flush-left "Name = {" span, nested table values close with "},",
/// and only the outermost assignment ends with a bare "}" on its own line. Measured against the
/// maintainer's real file: no line at any depth is indented, and the file contains exactly six bare
/// "}" lines for its six declared variables. So the distinguishing mark is the ABSENT trailing comma,
/// not the indentation — which is what makes this rule survive a WoW build that indents differently.
/// </summary>
public static class LootHistoryReader
{
    private const string Header = "KART_LootHistory = {";

    public static IReadOnlyList<LootHistoryEntry> Read(string fileText)
    {
        var lines = fileText.Replace("\r\n", "\n").Split('\n');

        var start = Array.FindIndex(lines, l => l == Header);
        if (start < 0) return Array.Empty<LootHistoryEntry>();

        var end = -1;
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i] == "}") { end = i; break; }
        }
        // No closing brace: the file was truncated mid-write, or it is not the shape we understand.
        // Either way, refusing the whole pass is the only safe answer — half an award list is
        // indistinguishable from a real one once it is in the archive.
        if (end < 0) throw new FormatException("KART_LootHistory block has no closing brace.");

        var entries = new List<LootHistoryEntry>();
        Dictionary<string, object?>? current = null;
        var nested = 0;
        string? nestedKey = null;
        Dictionary<string, object?>? nestedTable = null;

        for (var i = start + 1; i < end; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;

            if (line == "{")
            {
                if (current != null) throw new FormatException("Unexpected nested award table.");
                current = new Dictionary<string, object?>();
                continue;
            }

            if (line is "}," or "}")
            {
                if (nested > 0)
                {
                    current!.Add(nestedKey!, nestedTable);
                    nested = 0;
                    nestedKey = null;
                    nestedTable = null;
                    continue;
                }
                if (current == null) throw new FormatException("Closing brace without an open award.");
                entries.Add(new LootHistoryEntry(current));
                current = null;
                continue;
            }

            var (key, value, opensTable) = ParseAssignment(line);

            if (opensTable)
            {
                nested = 1;
                nestedKey = key;
                nestedTable = new Dictionary<string, object?>();
                continue;
            }

            if (nested > 0) nestedTable!.Add(key, value);
            else if (current != null) current.Add(key, value);
            else throw new FormatException($"Assignment outside an award table: {line}");
        }

        if (current != null) throw new FormatException("KART_LootHistory ended mid-award.");

        return entries;
    }

    private static (string Key, object? Value, bool OpensTable) ParseAssignment(string line)
    {
        // ["key"] = value,
        if (!line.StartsWith("[\"", StringComparison.Ordinal))
            throw new FormatException($"Unrecognised line: {line}");

        var keyEnd = line.IndexOf("\"] = ", StringComparison.Ordinal);
        if (keyEnd < 0) throw new FormatException($"Unrecognised line: {line}");

        var key = Unescape(line.Substring(2, keyEnd - 2));
        var rest = line[(keyEnd + 5)..].TrimEnd();

        if (rest == "{") return (key, null, true);

        if (rest.EndsWith(",", StringComparison.Ordinal)) rest = rest[..^1];

        if (rest == "true") return (key, true, false);
        if (rest == "false") return (key, false, false);
        if (rest == "nil") return (key, null, false);

        if (rest.StartsWith("\"", StringComparison.Ordinal) && rest.EndsWith("\"", StringComparison.Ordinal))
            return (key, Unescape(rest[1..^1]), false);

        // Everything else is a number. WoW writes them with an invariant decimal point.
        if (double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return (key, num, false);

        throw new FormatException($"Unrecognised value: {rest}");
    }

    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', var c => c });
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
