namespace AuthorPlus.Core.Services;

/// <summary>One line of a bullet field: what it says, and whether the AI put it there.</summary>
/// <param name="Text">The entry without its marker.</param>
/// <param name="FromAi">True when the line was added by the AI and the author has not taken the mark off.</param>
public readonly record struct Bullet(string Text, bool FromAi);

/// <summary>
/// The long fields on a character or a plotline are kept as plain text, one entry per line, so the
/// author can always open the whole field and write freely. A line that begins "(AI)" was put
/// there by the AI: the app shows it with that mark, and the author can edit it, drop the mark by
/// deleting those three characters, or delete the line. Nothing else about the text is special —
/// an old single-paragraph field simply reads as one bullet.
/// </summary>
public static class Bullets
{
    /// <summary>The mark that opens a line the AI added.</summary>
    public const string AiMark = "(AI)";

    /// <summary>The field's entries, in order, blank lines dropped.</summary>
    public static IReadOnlyList<Bullet> Parse(string? field)
    {
        var list = new List<Bullet>();
        if (string.IsNullOrWhiteSpace(field)) return list;
        foreach (var raw in field.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            line = TrimLeadingBullet(line);
            if (line.StartsWith(AiMark, StringComparison.OrdinalIgnoreCase))
                list.Add(new Bullet(line[AiMark.Length..].TrimStart(' ', ':', '-', '—'), true));
            else if (line.Length > 0)
                list.Add(new Bullet(line, false));
        }
        return list;
    }

    /// <summary>The entries back as field text, ready to store.</summary>
    public static string Join(IEnumerable<Bullet> bullets) =>
        string.Join("\n", bullets.Select(b => b.FromAi ? $"{AiMark} {b.Text}" : b.Text));

    /// <summary>
    /// Adds an entry to the end of a field, marked as the AI's when <paramref name="fromAi"/>.
    /// An entry the field already carries — whoever wrote it — is not added twice.
    /// </summary>
    public static string Add(string? field, string text, bool fromAi)
    {
        text = TrimLeadingBullet(text.Trim());
        if (text.StartsWith(AiMark, StringComparison.OrdinalIgnoreCase)) text = text[AiMark.Length..].TrimStart(' ', ':', '-', '—');
        if (text.Length == 0) return field ?? string.Empty;

        var existing = Parse(field);
        if (existing.Any(b => string.Equals(b.Text, text, StringComparison.OrdinalIgnoreCase))) return field ?? string.Empty;
        var line = fromAi ? $"{AiMark} {text}" : text;
        return existing.Count == 0 ? line : Join(existing) + "\n" + line;
    }

    /// <summary>Adds several entries in one go (each one skipped if the field already says it).</summary>
    public static string AddRange(string? field, IEnumerable<string> lines, bool fromAi)
    {
        var text = field ?? string.Empty;
        foreach (var line in lines) text = Add(text, line, fromAi);
        return text;
    }

    /// <summary>How many entries the AI put there and the author has kept.</summary>
    public static int AiCount(string? field) => Parse(field).Count(b => b.FromAi);

    /// <summary>A leading "-", "*" or "•" the author (or a model) may have typed is not part of the entry.</summary>
    private static string TrimLeadingBullet(string line) =>
        line.Length > 1 && (line[0] == '-' || line[0] == '*' || line[0] == '•') && char.IsWhiteSpace(line[1])
            ? line[2..].TrimStart()
            : line;
}

/// <summary>
/// Replies that come back as "LABEL | one fact" (or "LABEL: one fact"), one fact per line — the
/// shape the character and plotline prompts ask for, so each line can be filed under the right
/// field and added as its own entry. Anything that does not begin with a known label is dropped:
/// a stray sentence of preamble must never land in the author's fields.
/// </summary>
public static class LabeledLines
{
    public static IReadOnlyList<(string Label, string Text)> Parse(string? reply, IEnumerable<string> labels)
    {
        var known = labels.ToDictionary(l => l.ToLowerInvariant(), l => l);
        var found = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(reply)) return found;

        foreach (var raw in reply.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim().TrimStart('-', '*', '•', ' ').Trim();
            if (line.Length == 0) continue;
            int cut = line.IndexOfAny(new[] { '|', ':' });
            if (cut <= 0) continue;
            var label = line[..cut].Trim().Trim('*', '#', ' ').ToLowerInvariant();
            var text = line[(cut + 1)..].Trim().Trim('|').Trim();
            if (text.Length == 0 || !known.TryGetValue(label, out var canonical)) continue;
            found.Add((canonical, text));
        }
        return found;
    }
}
