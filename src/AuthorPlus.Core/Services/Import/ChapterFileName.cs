using System.Text.RegularExpressions;

namespace AuthorPlus.Core.Services.Import;

/// <summary>
/// Understands the file names authors give chapter files: "Chapter 12 - The Game.docx",
/// "Chapter Twenty-Eight - The Final Article.docx", "Chapter 14- The Game.docx",
/// "Ch 3: Landfall.docx", "12 The Game.docx". The number may be digits or English words;
/// the title is whatever follows the first dash, colon or en/em dash.
/// </summary>
public static class ChapterFileName
{
    private static readonly Regex Shape = new(
        @"^\s*(?:chapter|chap\.?|ch\.?)?\s*(?<num>\d{1,3}|[a-z]+(?:[- ][a-z]+)?)\s*(?:[-–—:.]\s*|\s+)(?<title>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, int> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
        ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70,
        ["eighty"] = 80, ["ninety"] = 90, ["hundred"] = 100
    };

    public sealed record Parsed(int Number, string Title);

    /// <summary>Parses a file name (with or without extension). Null when it is not a chapter file.</summary>
    public static Parsed? TryParse(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var m = Shape.Match(stem);
        if (!m.Success) return null;

        var numText = m.Groups["num"].Value;
        int number;
        if (int.TryParse(numText, out var digits)) number = digits;
        else if (!TryParseWords(numText, out number)) return null;

        var title = m.Groups["title"].Value.Trim();
        if (title.Length == 0) return null;
        // "Chapter 12 - The Game - Google Docs" → drop a trailing export suffix.
        title = Regex.Replace(title, @"\s*[-–—]\s*Google Docs\s*$", "", RegexOptions.IgnoreCase);
        return new Parsed(number, title);
    }

    /// <summary>"Twenty-Eight", "twenty eight", "Nine" → 28, 28, 9.</summary>
    public static bool TryParseWords(string text, out int value)
    {
        value = 0;
        var parts = text.Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 3) return false;
        int total = 0;
        foreach (var part in parts)
        {
            if (!Words.TryGetValue(part, out var v)) return false;
            if (v == 100) { if (total == 0) return false; total *= 100; }
            else total += v;
        }
        value = total;
        return total > 0;
    }
}
