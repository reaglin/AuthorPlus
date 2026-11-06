using System.Text.RegularExpressions;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>What is sent to the voice: the words (respelled where needed) and the exact phonetics for the voices that take them.</summary>
public sealed record SpokenText(string Text, IReadOnlyList<(string Phrase, string Ipa)> Phonetics)
{
    /// <summary>Everything that changes how it sounds, for the name of the file it is kept in.</summary>
    public string Key => Phonetics.Count == 0 ? Text : Text + "|" + string.Join(";", Phonetics.Select(p => p.Phrase + "=" + p.Ipa));
}

/// <summary>
/// The book's pronunciation list applied to words about to be spoken (plan 10.2). Whole words only
/// ("Dalgo's" is Dalgo; "Dalgoan" is not), the longest entry first (so "Gull Point" wins over "Gull"),
/// case-sensitive for entries written with a capital (the name Mark, not the word mark). Where the voice
/// takes exact phonetics and the entry has them, the words stay as written and the phonetics go with
/// them; otherwise the respelling is spoken in their place.
/// </summary>
public static class Pronunciations
{
    public static SpokenText Apply(string text, IEnumerable<Pronunciation> list, bool voiceTakesIpa)
    {
        var entries = list.Where(p => p.Word.Trim().Length > 0 && (p.SayAs.Trim().Length > 0 || p.Ipa.Trim().Length > 0))
                          .OrderByDescending(p => p.Word.Trim().Length).ToList();
        if (entries.Count == 0 || text.Length == 0) return new SpokenText(text, Array.Empty<(string, string)>());

        var taken = new bool[text.Length];
        var hits = new List<(int Start, int Length, Pronunciation P)>();
        foreach (var p in entries)
        {
            var word = p.Word.Trim();
            var options = word.Any(char.IsUpper) ? RegexOptions.None : RegexOptions.IgnoreCase;
            foreach (Match m in Regex.Matches(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"(?![\p{L}\p{N}])", options))
            {
                if (Enumerable.Range(m.Index, m.Length).Any(i => taken[i])) continue;
                for (int i = m.Index; i < m.Index + m.Length; i++) taken[i] = true;
                hits.Add((m.Index, m.Length, p));
            }
        }
        if (hits.Count == 0) return new SpokenText(text, Array.Empty<(string, string)>());

        var phonetics = new List<(string, string)>();
        var sb = new System.Text.StringBuilder();
        int at = 0;
        foreach (var (start, length, p) in hits.OrderBy(h => h.Start))
        {
            sb.Append(text, at, start - at);
            var asWritten = text.Substring(start, length);
            if (voiceTakesIpa && p.Ipa.Trim().Length > 0)
            {
                sb.Append(asWritten);
                if (!phonetics.Contains((asWritten, p.Ipa.Trim()))) phonetics.Add((asWritten, p.Ipa.Trim()));
            }
            else if (p.SayAs.Trim().Length > 0) sb.Append(Respelling(p.SayAs));
            else sb.Append(asWritten);
            at = start + length;
        }
        sb.Append(text, at, text.Length - at);
        return new SpokenText(sb.ToString(), phonetics);
    }

    /// <summary>
    /// A respelling as a voice reads it well: lower case (capitals in "oh-leh-meh-LOO-kway" mark the stress for
    /// the author, but a voice may spell capitals out letter by letter), hyphens kept between syllables.
    /// </summary>
    public static string Respelling(string sayAs) => Regex.Replace(sayAs.Trim(), @"\s+", " ").ToLowerInvariant();

    /// <summary>
    /// The words of the book worth a pronunciation: characters' names and also-calleds, and capitalised words the
    /// book uses often — not those on the list, and not those the AI was already asked about.
    /// </summary>
    public static IReadOnlyList<string> Candidates(Book book, IEnumerable<string> chapterTexts)
    {
        var names = new List<string>();
        foreach (var c in book.Characters)
        {
            names.AddRange(c.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2));
            names.AddRange(Bullets.Parse(c.Aliases).Select(a => a.Text));
        }
        var found = NameScan.Candidates(chapterTexts.SelectMany(t => t.Split('\n')).ToList(), book.Characters, book.NotNames);
        names.AddRange(found.Select(f => f.Name));
        var known = book.Pronunciations.Select(p => p.Word.Trim()).Concat(book.PronunciationsAsked).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Select(n => n.Trim().Trim('.', ',')).Where(n => n.Length > 1 && !known.Contains(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly Regex Line = new(@"^\s*[-*•]?\s*(?<word>[^|]+?)\s*\|\s*(?<say>[^|]*?)\s*\|\s*(?<ipa>[^|]*?)\s*(\|\s*(?<note>.*?))?\s*$", RegexOptions.Multiline);

    /// <summary>The AI's "Word | say it as | IPA | why" lines; words it says need nothing ("as written") are left out.</summary>
    public static IReadOnlyList<Pronunciation> Parse(string? reply)
    {
        var list = new List<Pronunciation>();
        foreach (Match m in Line.Matches(reply ?? ""))
        {
            var word = m.Groups["word"].Value.Trim().Trim('*', '"');
            var say = m.Groups["say"].Value.Trim();
            var ipa = m.Groups["ipa"].Value.Trim().Trim('/', '[', ']');
            if (word.Length == 0 || word.Equals("word", StringComparison.OrdinalIgnoreCase)) continue;
            if (say.Equals("as written", StringComparison.OrdinalIgnoreCase)) say = "";
            if (ipa == "-" || ipa.Equals("none", StringComparison.OrdinalIgnoreCase)) ipa = "";
            if (say.Length == 0 && ipa.Length == 0) continue;
            list.Add(new Pronunciation { Word = word, SayAs = say, Ipa = ipa, Note = m.Groups["note"].Value.Trim(), FromAi = true });
        }
        return list;
    }
}
