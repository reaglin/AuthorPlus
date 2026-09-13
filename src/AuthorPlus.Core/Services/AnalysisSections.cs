using System.Text.RegularExpressions;

namespace AuthorPlus.Core.Services;

/// <summary>One aspect of an analysis ("Pacing") and what the editor said about it.</summary>
public sealed record AnalysisSection(string Heading, string Body)
{
    /// <summary>The individual points under this aspect (each can get its own suggestions).</summary>
    public IReadOnlyList<string> Points => AnalysisSections.Points(Body);
}

/// <summary>
/// Splits an analysis into its aspects, and an aspect into its points. The prompt asks for
/// <c>## Heading</c> lines and numbered points; older analyses (or a model that ignores the
/// format) come back as one section with no heading, and a body without bullets is split at
/// blank lines, so nothing is ever hidden.
/// </summary>
public static class AnalysisSections
{
    private static readonly Regex HeadingRx = new(@"^\s{0,3}#{1,4}\s*(?<h>[^#\r\n]+?)\s*#*\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex PointStartRx = new(@"^\s*(?:[-*•]|\d{1,2}[.)])\s+", RegexOptions.Compiled);

    /// <summary>The aspects the chapter-analysis prompt is asked for, in order.</summary>
    public static readonly IReadOnlyList<string> ChapterAspects = new[]
    {
        "Pacing", "Tension", "Stakes", "Point of view and voice", "Dialogue", "Continuity", "Prose habits", "Three changes"
    };

    public static IReadOnlyList<AnalysisSection> Parse(string? body)
    {
        var text = (body ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return Array.Empty<AnalysisSection>();

        var matches = HeadingRx.Matches(text);
        if (matches.Count == 0) return new[] { new AnalysisSection("", text) };

        var result = new List<AnalysisSection>();
        if (matches[0].Index > 0)
        {
            var intro = text[..matches[0].Index].Trim();
            if (intro.Length > 0) result.Add(new AnalysisSection("", intro));
        }
        for (int i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var heading = matches[i].Groups["h"].Value.Trim().TrimEnd(':');
            result.Add(new AnalysisSection(heading, text[start..end].Trim()));
        }
        return result;
    }

    /// <summary>
    /// Bullet or numbered lines each start a point (continuation lines join them); a body with no
    /// bullets is split at blank lines. Markers are stripped from the returned points.
    /// </summary>
    public static IReadOnlyList<string> Points(string? body)
    {
        var text = (body ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return Array.Empty<string>();
        var lines = text.Split('\n');
        if (!lines.Any(l => PointStartRx.IsMatch(l)))
            return text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

        var points = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (PointStartRx.IsMatch(line))
            {
                if (current.Length > 0) points.Add(current.ToString().Trim());
                current.Clear();
                current.Append(PointStartRx.Replace(line, "", 1).Trim());
            }
            else if (line.Trim().Length == 0)
            {
                if (current.Length > 0) { points.Add(current.ToString().Trim()); current.Clear(); }
            }
            else
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(line.Trim());
            }
        }
        if (current.Length > 0) points.Add(current.ToString().Trim());
        return points.Where(p => p.Length > 0).ToList();
    }
}

/// <summary>A character or plotline an AI pass found. For plotlines, <see cref="Kind"/> and <see cref="ChapterNumbers"/> may be set.</summary>
public sealed record ExtractedEntity(string Name, bool IsNew, bool IsPov, string Note, string Kind = "", IReadOnlyList<int>? ChapterNumbers = null)
{
    public IReadOnlyList<int> Chapters => ChapterNumbers ?? Array.Empty<int>();
    public bool IsPrimary => Kind.StartsWith("prim", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What the <c>chapter-extract</c> prompt returns, parsed.</summary>
public sealed class ChapterExtract
{
    public List<ExtractedEntity> Characters { get; } = new();
    public List<ExtractedEntity> Plotlines { get; } = new();

    private static readonly string[] Kinds = { "primary", "secondary", "subplot", "main", "side" };

    internal static string NormalizeKind(string s)
    {
        var k = s.Trim().ToLowerInvariant();
        if (k.StartsWith("prim") || k == "main" || k == "spine") return "primary";
        if (k.StartsWith("second")) return "secondary";
        if (k.StartsWith("sub") || k == "side" || k == "minor") return "subplot";
        return "";
    }

    internal static bool LooksLikeKind(string s) => Kinds.Any(k => s.Trim().StartsWith(k, StringComparison.OrdinalIgnoreCase)) || s.Trim().Equals("spine", StringComparison.OrdinalIgnoreCase) || s.Trim().Equals("minor", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses the two blocks the prompt asks for:
    /// <code>
    /// CHARACTERS
    /// Name | known or new | POV or - | what they do
    /// PLOTLINES
    /// Name | known or new | primary or secondary or subplot | what happens in this thread here
    /// </code>
    /// Tolerant of bullets, numbering and missing columns; unknown lines are skipped.
    /// </summary>
    public static ChapterExtract Parse(string? text)
    {
        var result = new ChapterExtract();
        List<ExtractedEntity>? target = null;
        bool plotBlock = false;
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = CleanLine(raw);
            if (line.Length == 0) continue;
            var upper = line.TrimEnd(':').ToUpperInvariant();
            if (upper is "CHARACTERS" or "CHARACTER") { target = result.Characters; plotBlock = false; continue; }
            if (upper is "PLOTLINES" or "PLOTLINE" or "PLOT LINES") { target = result.Plotlines; plotBlock = true; continue; }
            if (target == null) continue;
            if (IsNone(line)) continue;

            var cols = line.Split('|').Select(c => c.Trim()).ToList();
            if (cols.Count < 2 || cols[0].Length == 0 || cols[0].Length > 80) continue;
            var name = cols[0].Trim('*', '_', '"', '“', '”');
            bool isNew = cols[1].Contains("new", StringComparison.OrdinalIgnoreCase);
            if (target.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

            if (!plotBlock)
            {
                bool pov = cols.Count >= 3 && cols[2].Contains("POV", StringComparison.OrdinalIgnoreCase);
                var note = cols.Count >= 4 ? cols[3] : cols.Count == 3 && !pov ? cols[2] : "";
                target.Add(new ExtractedEntity(name, isNew, pov, note));
            }
            else
            {
                var rest = cols.Skip(2).ToList();
                string kind = "";
                if (rest.Count > 0 && LooksLikeKind(rest[0])) { kind = NormalizeKind(rest[0]); rest.RemoveAt(0); }
                target.Add(new ExtractedEntity(name, isNew, false, string.Join(" | ", rest), kind));
            }
        }
        return result;
    }

    internal static string CleanLine(string raw)
    {
        var line = raw.Trim().TrimStart('-', '*', '•', ' ');
        return Regex.Replace(line, @"^\d+[.)]\s*", "");
    }

    internal static bool IsNone(string line) =>
        line.StartsWith("(none", StringComparison.OrdinalIgnoreCase) || line.Equals("none", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What the <c>find-plotlines</c> prompt returns over a section or the book:
/// <code>
/// Name | known or new | primary or secondary or subplot | chapters: 1, 3, 7 | one-line description
/// </code>
/// </summary>
public static class PlotlineFinder
{
    private static readonly Regex NumberRx = new(@"\d{1,3}", RegexOptions.Compiled);
    /// <summary>"chapters: 1, 2, 3", "ch. 3, 11", "4, 12", "7–9 and 12" — a list of chapter numbers and nothing else.</summary>
    private static readonly Regex ChapterListRx = new(@"^(?:chapters?\.?:?\s*|ch\.?:?\s*)?[\d,\s\-–and&]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<ExtractedEntity> Parse(string? text)
    {
        var result = new List<ExtractedEntity>();
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = ChapterExtract.CleanLine(raw);
            if (line.Length == 0 || ChapterExtract.IsNone(line)) continue;
            if (line.TrimEnd(':').Equals("PLOTLINES", StringComparison.OrdinalIgnoreCase)) continue;
            var cols = line.Split('|').Select(c => c.Trim()).ToList();
            if (cols.Count < 3 || cols[0].Length == 0 || cols[0].Length > 80) continue;
            var name = cols[0].Trim('*', '_', '"', '“', '”');
            if (result.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            bool isNew = cols[1].Contains("new", StringComparison.OrdinalIgnoreCase);
            var rest = cols.Skip(2).ToList();
            string kind = "";
            if (rest.Count > 0 && ChapterExtract.LooksLikeKind(rest[0])) { kind = ChapterExtract.NormalizeKind(rest[0]); rest.RemoveAt(0); }
            var chapters = new List<int>();
            int chapterCol = rest.FindIndex(c => ChapterListRx.IsMatch(c));
            if (chapterCol >= 0)
            {
                chapters = NumberRx.Matches(rest[chapterCol]).Select(m => int.Parse(m.Value)).Distinct().ToList();
                rest.RemoveAt(chapterCol);
            }
            result.Add(new ExtractedEntity(name, isNew, false, string.Join(" | ", rest), kind, chapters));
        }
        return result;
    }
}
