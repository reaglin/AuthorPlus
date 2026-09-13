using System.Text.RegularExpressions;

namespace AuthorPlus.Core.Services;

/// <summary>One aspect of an analysis ("Pacing") and what the editor said about it.</summary>
public sealed record AnalysisSection(string Heading, string Body);

/// <summary>
/// Splits an analysis into its aspects. The prompt asks for <c>## Heading</c> lines; older
/// analyses (or a model that ignores the format) come back as one section with no heading,
/// so nothing is ever hidden.
/// </summary>
public static class AnalysisSections
{
    private static readonly Regex HeadingRx = new(@"^\s{0,3}#{1,4}\s*(?<h>[^#\r\n]+?)\s*#*\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

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
}

/// <summary>A character or plotline the extraction pass found in a chapter.</summary>
public sealed record ExtractedEntity(string Name, bool IsNew, bool IsPov, string Note);

/// <summary>What the <c>chapter-extract</c> prompt returns, parsed.</summary>
public sealed class ChapterExtract
{
    public List<ExtractedEntity> Characters { get; } = new();
    public List<ExtractedEntity> Plotlines { get; } = new();

    /// <summary>
    /// Parses the two blocks the prompt asks for:
    /// <code>
    /// CHARACTERS
    /// Name | known or new | POV or - | what they do
    /// PLOTLINES
    /// Name | known or new | what happens in this thread here
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
            var line = raw.Trim().TrimStart('-', '*', '•', ' ');
            line = Regex.Replace(line, @"^\d+[.)]\s*", "");
            if (line.Length == 0) continue;
            var upper = line.TrimEnd(':').ToUpperInvariant();
            if (upper is "CHARACTERS" or "CHARACTER") { target = result.Characters; plotBlock = false; continue; }
            if (upper is "PLOTLINES" or "PLOTLINE" or "PLOT LINES") { target = result.Plotlines; plotBlock = true; continue; }
            if (target == null) continue;
            if (line.StartsWith("(none", StringComparison.OrdinalIgnoreCase) || line.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;

            var cols = line.Split('|').Select(c => c.Trim()).ToList();
            if (cols.Count < 2 || cols[0].Length == 0 || cols[0].Length > 80) continue;
            var name = cols[0].Trim('*', '_', '"', '“', '”');
            bool isNew = cols[1].Contains("new", StringComparison.OrdinalIgnoreCase);
            bool pov = !plotBlock && cols.Count >= 3 && cols[2].Contains("POV", StringComparison.OrdinalIgnoreCase);
            var note = plotBlock ? (cols.Count >= 3 ? cols[2] : "") : (cols.Count >= 4 ? cols[3] : cols.Count == 3 && !pov ? cols[2] : "");
            if (target.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            target.Add(new ExtractedEntity(name, isNew, pov, note));
        }
        return result;
    }
}
