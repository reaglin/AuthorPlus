using System.Text;
using System.Text.RegularExpressions;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>
/// Parses the <c>analysis-suggestions</c> reply:
/// <code>
/// SUGGESTION 1
/// ORIGINAL:
/// …the passage exactly as it appears in the chapter…
/// REWRITE:
/// …the proposed replacement…
/// WHY:
/// …one or two sentences…
/// </code>
/// Tolerant of "### Suggestion 1", bold labels, and missing WHY; a suggestion without both an
/// original and a rewrite is skipped.
/// </summary>
public static class SuggestionParser
{
    private static readonly Regex StartRx = new(@"^\s*(?:#+\s*|\*\*)?suggestion\s*(?<n>\d+)?\s*[:.)\-–—]?\s*(?:\*\*)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LabelRx = new(@"^\s*(?:\*\*|__)?\s*(?<l>original|current|passage|rewrite|rewritten|revised|suggested|why|reason|reasoning)\s*(?:\*\*|__)?\s*:\s*(?:\*\*|__)?\s*(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<SuggestionEntry> Parse(string? text)
    {
        var entries = new List<SuggestionEntry>();
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
        SuggestionEntry? current = null;
        string field = "";
        var buffers = new Dictionary<string, StringBuilder>();

        void Flush()
        {
            if (current == null) return;
            current.Original = Clean(buffers.GetValueOrDefault("original")?.ToString());
            current.Rewrite  = Clean(buffers.GetValueOrDefault("rewrite")?.ToString());
            current.Why      = Clean(buffers.GetValueOrDefault("why")?.ToString());
            if (current.Original.Length > 0 && current.Rewrite.Length > 0) entries.Add(current);
            current = null; field = ""; buffers.Clear();
        }

        foreach (var raw in lines)
        {
            var m = StartRx.Match(raw);
            if (m.Success)
            {
                Flush();
                current = new SuggestionEntry { Index = int.TryParse(m.Groups["n"].Value, out var n) ? n : entries.Count + 1 };
                continue;
            }
            if (current == null) continue;
            var lm = LabelRx.Match(raw);
            if (lm.Success)
            {
                var l = lm.Groups["l"].Value.ToLowerInvariant();
                field = l is "original" or "current" or "passage" ? "original" : l is "why" or "reason" or "reasoning" ? "why" : "rewrite";
                buffers[field] = new StringBuilder();
                if (lm.Groups["rest"].Value.Trim().Length > 0) buffers[field].Append(lm.Groups["rest"].Value).Append('\n');
                continue;
            }
            if (field.Length > 0) buffers[field].Append(raw).Append('\n');
        }
        Flush();
        for (int i = 0; i < entries.Count; i++) entries[i].Index = i + 1;
        return entries;
    }

    /// <summary>Strips surrounding quotes, code fences and blank edges; keeps inner line breaks.</summary>
    internal static string Clean(string? s)
    {
        var t = (s ?? "").Trim();
        t = Regex.Replace(t, @"^```\w*\s*|\s*```$", "");
        t = t.Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '“' && t[^1] == '”')) && t.IndexOf('\n') < 0) t = t[1..^1].Trim();
        return t;
    }
}

/// <summary>One piece of a word-level diff.</summary>
public enum DiffKind { Equal, Removed, Added }
public sealed record DiffPiece(string Text, DiffKind Kind);

/// <summary>
/// Word-level diff (longest common subsequence over words) so a rewrite can be shown with the
/// removed words highlighted on the left and the added words on the right. Whitespace is kept
/// with the word before it so the pieces join back into the original strings.
/// </summary>
public static class WordDiff
{
    private static readonly Regex TokenRx = new(@"\S+\s*", RegexOptions.Compiled);

    public static IReadOnlyList<DiffPiece> Compute(string before, string after)
    {
        var a = TokenRx.Matches(before ?? "").Select(m => m.Value).ToList();
        var b = TokenRx.Matches(after ?? "").Select(m => m.Value).ToList();
        static string Key(string t) => t.Trim().ToLowerInvariant();

        var lcs = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
            for (int j = b.Count - 1; j >= 0; j--)
                lcs[i, j] = Key(a[i]) == Key(b[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<DiffPiece>();
        int x = 0, y = 0;
        while (x < a.Count && y < b.Count)
        {
            if (Key(a[x]) == Key(b[y])) { Add(result, b[y], DiffKind.Equal); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { Add(result, a[x], DiffKind.Removed); x++; }
            else { Add(result, b[y], DiffKind.Added); y++; }
        }
        while (x < a.Count) Add(result, a[x++], DiffKind.Removed);
        while (y < b.Count) Add(result, b[y++], DiffKind.Added);
        return result;
    }

    private static void Add(List<DiffPiece> list, string text, DiffKind kind)
    {
        if (list.Count > 0 && list[^1].Kind == kind) list[^1] = new DiffPiece(list[^1].Text + text, kind);
        else list.Add(new DiffPiece(text, kind));
    }

    /// <summary>The left side: equal and removed pieces (the text as it is).</summary>
    public static IEnumerable<DiffPiece> Left(IReadOnlyList<DiffPiece> d) => d.Where(p => p.Kind != DiffKind.Added);
    /// <summary>The right side: equal and added pieces (the text as proposed).</summary>
    public static IEnumerable<DiffPiece> Right(IReadOnlyList<DiffPiece> d) => d.Where(p => p.Kind != DiffKind.Removed);
}

/// <summary>
/// Finds a quoted passage in a chapter's text even when whitespace, quote marks or dashes differ
/// (models normalise them; Word uses curly quotes). Returns the span in the original text.
/// </summary>
public static class PassageLocator
{
    public sealed record Span(int Start, int Length);

    /// <summary>Exact-after-normalisation match; then the first 60 characters; null when neither is found.</summary>
    public static Span? Find(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrWhiteSpace(needle)) return null;
        var (hNorm, map) = Normalize(haystack);
        var (nNorm, _) = Normalize(needle);
        if (nNorm.Length == 0) return null;

        var idx = hNorm.IndexOf(nNorm, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 && nNorm.Length > 60)
        {
            var head = nNorm[..60].TrimEnd();
            idx = hNorm.IndexOf(head, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) nNorm = head;
        }
        if (idx < 0) return null;

        int start = map[idx];
        int endExclusive = map[idx + nNorm.Length - 1] + 1;
        return new Span(start, endExclusive - start);
    }

    /// <summary>Collapses whitespace runs to one space, straightens quotes and dashes; returns the map from normalised index to original index.</summary>
    internal static (string Text, List<int> Map) Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        var map = new List<int>(s.Length);
        bool pendingSpace = false;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); map.Add(i - 1); pendingSpace = false; }
            sb.Append(c switch
            {
                '“' or '”' or '„' => '"',
                '‘' or '’' or '‚' => '\'',
                '–' or '—' or '−' => '-',
                '…' => '.',
                _ => c
            });
            map.Add(i);
        }
        return (sb.ToString(), map);
    }
}
