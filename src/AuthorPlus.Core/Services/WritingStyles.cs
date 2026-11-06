using System.Text.Json;

namespace AuthorPlus.Core.Services;

/// <summary>
/// The styles the author has asked the AI to write in (Create mode), kept so they can be chosen
/// again: most recent first, each once. One list for every book, in
/// <c>Documents\AuthorPlus\writing-styles.json</c> — a style is the author's, not a book's.
/// </summary>
public sealed class WritingStyles
{
    public const int Max = 60;
    private readonly string _path;

    public WritingStyles(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AuthorPlus", "writing-styles.json");

    /// <summary>The saved styles, most recent first. A missing or unreadable file is an empty list.</summary>
    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<string>();
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new List<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Puts the style at the top of the list (moving it if it is there already). Blank styles are not kept.</summary>
    public void Remember(string? style)
    {
        var s = (style ?? "").Trim();
        if (s.Length == 0) return;
        var list = Load().Where(x => !x.Trim().Equals(s, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, s);
        Save(list.Take(Max));
    }

    /// <summary>Takes a style off the list.</summary>
    public void Forget(string style) =>
        Save(Load().Where(x => !x.Trim().Equals(style.Trim(), StringComparison.OrdinalIgnoreCase)));

    private void Save(IEnumerable<string> styles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(styles.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>What the AI wrote in Create mode, as paragraphs to put into the chapter.</summary>
public static class CreatedText
{
    /// <summary>
    /// The reply as paragraphs: code fences and a wrapping pair of quotes taken off, paragraphs split
    /// at blank lines (or at line ends when there are no blank lines), and a "Here is…" opening line dropped.
    /// </summary>
    public static IReadOnlyList<string> Paragraphs(string? reply)
    {
        var text = (reply ?? "").Replace("\r\n", "\n").Trim();
        if (text.StartsWith("```")) text = string.Join("\n", text.Split('\n').Skip(1).Where(l => !l.TrimStart().StartsWith("```")));
        var lines = text.Split('\n').ToList();
        if (lines.Count > 1 && lines[0].TrimEnd().EndsWith(':') && lines[0].Length < 120 &&
            (lines[0].StartsWith("Here", StringComparison.OrdinalIgnoreCase) || lines[0].StartsWith("Sure", StringComparison.OrdinalIgnoreCase)))
            lines.RemoveAt(0);
        text = string.Join("\n", lines).Trim();
        var parts = text.Contains("\n\n")
            ? System.Text.RegularExpressions.Regex.Split(text, @"\n[ \t]*\n+")
            : text.Split('\n');
        var paras = parts.Select(p => string.Join(" ", p.Split('\n').Select(l => l.Trim())).Trim()).Where(p => p.Length > 0).ToList();
        if (paras.Count > 0 && paras[0].StartsWith('"') && paras[^1].EndsWith('"') && paras.Sum(p => p.Count(c => c == '"')) == 2)
        {
            paras[0] = paras[0][1..]; paras[^1] = paras[^1][..^1];
        }
        return paras;
    }
}
