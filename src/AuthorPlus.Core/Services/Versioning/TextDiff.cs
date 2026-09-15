using AuthorPlus.Core.Services.Export;

namespace AuthorPlus.Core.Services.Versioning;

/// <summary>
/// Compares two versions of a chapter the way an author reads it: paragraph by paragraph, in the
/// prose, never in the FlowDocument XAML the prose is stored as. An untouched paragraph appears
/// once as <see cref="DiffKind.Equal"/>; a rewritten one appears twice, removed and added —
/// and the two can then be run through <see cref="WordDiff"/> to show which words moved, exactly
/// as a rewrite suggestion is shown.
///
/// The same longest-common-subsequence walk as <see cref="WordDiff"/>, over paragraphs rather
/// than words. A chapter is hundreds of paragraphs, not millions, so the plain table is both
/// fast enough and easy to be sure of.
/// </summary>
public static class TextDiff
{
    /// <summary>Two versions of a chapter body, compared. Either side may be null (it did not exist then).</summary>
    public static IReadOnlyList<DiffPiece> Chapters(string? beforeXaml, string? afterXaml) =>
        Paragraphs(Plain(beforeXaml), Plain(afterXaml));

    /// <summary>The prose of a chapter body, or an empty string when there is none.</summary>
    public static string Plain(string? xaml) =>
        string.IsNullOrWhiteSpace(xaml) ? string.Empty : FlowDocumentXaml.ToPlainText(xaml);

    public static IReadOnlyList<DiffPiece> Paragraphs(string before, string after)
    {
        var old_ = Split(before);
        var new_ = Split(after);

        // lcs[i, j] = how many paragraphs the two tails still have in common.
        var lcs = new int[old_.Count + 1, new_.Count + 1];
        for (int i = old_.Count - 1; i >= 0; i--)
            for (int j = new_.Count - 1; j >= 0; j--)
                lcs[i, j] = Same(old_[i], new_[j])
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<DiffPiece>();
        int x = 0, y = 0;
        while (x < old_.Count && y < new_.Count)
        {
            if (Same(old_[x], new_[y])) { result.Add(new DiffPiece(new_[y], DiffKind.Equal)); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { result.Add(new DiffPiece(old_[x], DiffKind.Removed)); x++; }
            else { result.Add(new DiffPiece(new_[y], DiffKind.Added)); y++; }
        }
        while (x < old_.Count) result.Add(new DiffPiece(old_[x++], DiffKind.Removed));
        while (y < new_.Count) result.Add(new DiffPiece(new_[y++], DiffKind.Added));
        return result;
    }

    /// <summary>How many paragraphs came in and how many went, for the one-line summary.</summary>
    public static (int Added, int Removed) Counts(IReadOnlyList<DiffPiece> paragraphs) =>
        (paragraphs.Count(p => p.Kind == DiffKind.Added), paragraphs.Count(p => p.Kind == DiffKind.Removed));

    private static List<string> Split(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

    /// <summary>Two paragraphs are the same paragraph when only their spacing differs.</summary>
    private static bool Same(string a, string b) =>
        string.Equals(Squeeze(a), Squeeze(b), StringComparison.Ordinal);

    private static string Squeeze(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        bool space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && builder.Length > 0) builder.Append(' ');
            space = false;
            builder.Append(c);
        }
        return builder.ToString();
    }
}
