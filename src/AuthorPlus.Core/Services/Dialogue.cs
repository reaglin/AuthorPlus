using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>A stretch of a paragraph: speech inside quotation marks, or the narration around it.</summary>
public sealed record DialogueSpan(int Start, int Length, string Text, bool IsQuote);

/// <summary>A line of dialogue in a chapter: which paragraph, where in it, its words, and which time those words are said.</summary>
public sealed record QuoteLine(int Paragraph, int Start, int Length, string Text, string Key, int Occurrence, string Context = "");

/// <summary>A piece of the chapter as Listen mode reads it: narration, or a line of dialogue with its speaker.</summary>
public sealed record ReadPart(int Paragraph, int Start, int Length, string Text, bool IsQuote, Guid? CharacterId);

/// <summary>
/// Dialogue in a chapter (Listen mode, audiobooks; plan phase 9): where the quotation marks are, who
/// says each line (<see cref="ItemKind.ChapterSpeakers"/>), and the chapter split into the parts each
/// voice reads.
/// </summary>
public static class Dialogue
{
    /// <summary>
    /// The paragraph split at its double quotation marks (straight or curly). A quotation left open at
    /// the end of the paragraph runs to the end — a speech that goes on into the next paragraph.
    /// </summary>
    public static IReadOnlyList<DialogueSpan> Split(string text)
    {
        var spans = new List<DialogueSpan>();
        int i = 0, from = 0;
        while (i < text.Length)
        {
            char c = text[i];
            bool opens = c == '“' || (c == '"' && (i == 0 || !char.IsLetterOrDigit(text[i - 1]) || IsOpeningContext(text, i)));
            if (!opens) { i++; continue; }
            int close = -1;
            for (int j = i + 1; j < text.Length; j++)
                if (text[j] == '”' || text[j] == '"') { close = j; break; }
            if (i > from) Add(spans, text, from, i - from, false);
            int end = close < 0 ? text.Length : close + 1;
            Add(spans, text, i, end - i, true);
            from = i = end;
        }
        if (from < text.Length) Add(spans, text, from, text.Length - from, false);
        return spans;
    }

    private static bool IsOpeningContext(string text, int i) => i > 0 && (char.IsWhiteSpace(text[i - 1]) || "([—–-".Contains(text[i - 1]));

    private static void Add(List<DialogueSpan> spans, string text, int start, int length, bool quote)
    {
        if (length <= 0) return;
        spans.Add(new DialogueSpan(start, length, text.Substring(start, length), quote));
    }

    /// <summary>The words of a quotation, as lines are matched: marks off, spaces tidied, curly apostrophes straight.</summary>
    public static string Key(string quote)
    {
        var t = quote.Trim().Trim('"', '“', '”').Replace('’', '\'').Replace('‘', '\'');
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    /// <summary>Every line of dialogue in the chapter's paragraphs, in order, each with its occurrence count.</summary>
    public static IReadOnlyList<QuoteLine> Lines(IReadOnlyList<string> paragraphs)
    {
        var seen = new Dictionary<string, int>();
        var lines = new List<QuoteLine>();
        for (int p = 0; p < paragraphs.Count; p++)
            foreach (var s in Split(paragraphs[p]).Where(s => s.IsQuote))
            {
                var key = Key(s.Text);
                if (!key.Any(char.IsLetterOrDigit)) continue;
                int n = seen.TryGetValue(key, out var k) ? k : 0;
                seen[key] = n + 1;
                lines.Add(new QuoteLine(p, s.Start, s.Length, s.Text, key, n, Context(paragraphs[p], s.Start, s.Length)));
            }
        return lines;
    }

    /// <summary>
    /// The words around a line in its paragraph — up to 40 characters before it and 40 after, spaces tidied —
    /// to find the line again when the same words are said elsewhere.
    /// </summary>
    public static string Context(string paragraph, int start, int length)
    {
        static string Tidy(string s) => Regex.Replace(s, @"\s+", " ").Trim();
        var before = Tidy(paragraph[..start]);
        var after = Tidy(paragraph[Math.Min(paragraph.Length, start + length)..]);
        return (before.Length <= 40 ? before : before[^40..]) + "|" + (after.Length <= 40 ? after : after[..40]);
    }

    /// <summary>
    /// Who says the line, from the chapter's speakers item; null when no one is recorded. The same words with the
    /// same words before them first (so adding an earlier "Yes." does not move the speakers along), then by count.
    /// </summary>
    public static SpokenLine? SpeakerOf(Item? speakers, QuoteLine line)
    {
        if (speakers == null) return null;
        var same = speakers.Lines.Where(l => l.Quote == line.Key).ToList();
        if (same.Count == 0) return null;
        var placed = same.Where(l => l.Context.Length > 0 && l.Context == line.Context).ToList();
        if (placed.Count == 1) return placed[0];
        if (placed.Count > 1) return placed.FirstOrDefault(l => l.Occurrence == line.Occurrence) ?? placed[0];
        // The words on one side still the same (a tag changed, a sentence added): the same line. Recorded before the
        // words around lines were kept: by count alone.
        static (string B, string A) Halves(string c) { int i = c.IndexOf('|'); return i < 0 ? (c, "") : (c[..i], c[(i + 1)..]); }
        var (lb, la) = Halves(line.Context);
        var half = same.Where(l => Halves(l.Context) is var (b, a) && ((b.Length > 0 && b == lb) || (a.Length > 0 && a == la))).ToList();
        if (half.Count > 0) return half.FirstOrDefault(l => l.Occurrence == line.Occurrence) ?? half[0];
        return same.FirstOrDefault(l => l.Context.Length == 0 && l.Occurrence == line.Occurrence);
    }

    /// <summary>Records who says a line (the author's choice wins over the AI's from then on).</summary>
    public static void SetSpeaker(Item speakers, QuoteLine line, Guid? characterId, string? name, bool byAuthor)
    {
        var existing = SpeakerOf(speakers, line);
        if (existing == null) speakers.Lines.Add(existing = new SpokenLine { Quote = line.Key, Occurrence = line.Occurrence });
        else if (existing.ByAuthor && !byAuthor) return;
        existing.Occurrence = line.Occurrence;
        existing.Context = line.Context;
        existing.CharacterId = characterId;
        existing.SpeakerName = characterId == null ? name : null;
        existing.ByAuthor = byAuthor;
    }

    /// <summary>
    /// The chapter for the AI: its paragraphs with ⟦n⟧ in front of each line of dialogue, numbered from 1
    /// in the order of <paramref name="lines"/>.
    /// </summary>
    public static string Marked(IReadOnlyList<string> paragraphs, IReadOnlyList<QuoteLine> lines)
    {
        var sb = new StringBuilder();
        for (int p = 0; p < paragraphs.Count; p++)
        {
            var text = paragraphs[p];
            var inPara = lines.Select((l, n) => (l, n)).Where(x => x.l.Paragraph == p).OrderByDescending(x => x.l.Start);
            foreach (var (l, n) in inPara) text = text.Insert(l.Start, $"⟦{n + 1}⟧");
            sb.Append(text).Append("\n\n");
        }
        return sb.ToString().TrimEnd();
    }

    private static readonly Regex Answer = new(@"^\s*[-*•]?\s*⟦?(\d+)⟧?\s*[|:.)\-–—]\s*(.+?)\s*$", RegexOptions.Multiline);

    /// <summary>The AI's "n | Name" lines: number → the name it gave ("Narrator" and "?" kept as given).</summary>
    public static IReadOnlyDictionary<int, string> ParseSpeakers(string? reply)
    {
        var map = new Dictionary<int, string>();
        foreach (Match m in Answer.Matches(reply ?? ""))
            if (int.TryParse(m.Groups[1].Value, out var n)) map[n] = m.Groups[2].Value.Trim().Trim('*').Trim();
        return map;
    }

    /// <summary>True for the AI's word for "no character": the narrator reads it.</summary>
    public static bool IsNarrator(string name) =>
        name.Equals("Narrator", StringComparison.OrdinalIgnoreCase) || name.Equals("narration", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("none", StringComparison.OrdinalIgnoreCase) || name.StartsWith("not speech", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the AI's "cannot tell".</summary>
    public static bool IsUnknown(string name) => name.Trim() is "?" or "" || name.Equals("unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>The character a name means: their name, an "also called", or their first name alone; null when none.</summary>
    public static Character? Match(string name, IEnumerable<Character> cast)
    {
        var n = Key(name).Trim('.', ',');
        var list = cast.ToList();
        return list.FirstOrDefault(c => c.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(c => Bullets.Parse(c.Aliases).Any(a => a.Text.Equals(n, StringComparison.OrdinalIgnoreCase)))
            ?? list.FirstOrDefault(c => c.Name.Split(' ')[0].Equals(n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The chapter as the voices read it: narration in the narrator's voice, each line of dialogue in its
    /// speaker's. Pieces with nothing to say (spaces, a lone comma) are left out.
    /// </summary>
    public static IReadOnlyList<ReadPart> Plan(IReadOnlyList<string> paragraphs, Item? speakers)
    {
        var parts = new List<ReadPart>();
        var seen = new Dictionary<string, int>();
        for (int p = 0; p < paragraphs.Count; p++)
            foreach (var s in Split(paragraphs[p]))
            {
                if (!s.Text.Any(char.IsLetterOrDigit)) continue;
                if (!s.IsQuote) { parts.Add(new ReadPart(p, s.Start, s.Length, s.Text.Trim(), false, null)); continue; }
                var key = Key(s.Text);
                int n = seen.TryGetValue(key, out var k) ? k : 0;
                seen[key] = n + 1;
                var who = SpeakerOf(speakers, new QuoteLine(p, s.Start, s.Length, s.Text, key, n, Context(paragraphs[p], s.Start, s.Length)));
                parts.Add(new ReadPart(p, s.Start, s.Length, key, true, who?.CharacterId));
            }
        return parts;
    }

    /// <summary>The name of the file a spoken piece is kept in: the same voice, pace and words are never paid for twice.</summary>
    public static string AudioKey(string voice, double rate, string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{voice}|{rate:0.00}|{text}"));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }
}
