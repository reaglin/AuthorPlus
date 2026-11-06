using System.Text.RegularExpressions;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>Where a paragraph stands in Analysis mode; the colour of its ¶ icon.</summary>
public enum ParagraphState
{
    /// <summary>Not read yet.</summary>
    None,
    /// <summary>Read, with nothing left open.</summary>
    Read,
    /// <summary>Read, and some rewrites are still waiting for the author.</summary>
    Open,
    /// <summary>The author has dealt with it: marked done, or every rewrite acted on.</summary>
    Done
}

/// <summary>
/// The paragraph reads of Analysis mode (<see cref="ItemKind.ParagraphAnalysis"/>): the reply's
/// "READ" part, which paragraph of the chapter a read belongs to now that the text may have
/// changed, and the state its icon shows.
/// </summary>
public static class ParagraphReads
{
    private static readonly Regex SuggestionStart = new(@"^\s*(?:#+\s*|\*\*)?suggestion\s*\d*", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex ReadLabel = new(@"^\s*(?:#+\s*)?(?:\*\*)?read(?:\*\*)?\s*:?\s*(?:\*\*)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>The words-only part of the reply: everything before the first SUGGESTION, without its "READ:" label.</summary>
    public static string ReadPart(string? body)
    {
        var text = (body ?? "").Replace("\r\n", "\n");
        var m = SuggestionStart.Match(text);
        if (m.Success) text = text[..m.Index];
        text = ReadLabel.Replace(text, "", 1);
        return text.Trim();
    }

    /// <summary>
    /// Which of <paramref name="paragraphs"/> a read belongs to: the paragraph with exactly its
    /// text (the one nearest where it was, if the text repeats), otherwise the most similar one
    /// — at least half its words shared — nearest where it was. -1 when none is close enough.
    /// <c>Exact</c> is false when the paragraph has been changed since it was read.
    /// </summary>
    public static (int Index, bool Exact) Match(IReadOnlyList<string> paragraphs, string passage, int? hint)
    {
        var key = Normal(passage);
        if (key.Length == 0) return (-1, false);
        int at = hint ?? 0;

        int best = -1;
        for (int i = 0; i < paragraphs.Count; i++)
            if (Normal(paragraphs[i]) == key && (best < 0 || Math.Abs(i - at) < Math.Abs(best - at))) best = i;
        if (best >= 0) return (best, true);

        var words = Words(passage);
        double bestScore = 0;
        for (int i = 0; i < paragraphs.Count; i++)
        {
            var score = Similarity(words, Words(paragraphs[i]));
            if (score < 0.5) continue;
            score -= Math.Abs(i - at) * 0.001;                  // a tie goes to the nearer paragraph
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return (best, false);
    }

    /// <summary>
    /// The state of a paragraph from the reads made of it. Reads the author marked "dealt with"
    /// are set aside; what is left decides — so a newer read with rewrites waiting shows as
    /// waiting even when an older one was dealt with.
    /// </summary>
    public static ParagraphState StateOf(IEnumerable<Item> reads)
    {
        var list = reads.ToList();
        if (list.Count == 0) return ParagraphState.None;
        var live = list.Where(r => !r.Resolved).ToList();
        if (live.Count == 0) return ParagraphState.Done;
        var all = live.SelectMany(r => r.Suggestions).ToList();
        if (all.Any(s => s.Status == SuggestionStatus.Open)) return ParagraphState.Open;
        if (all.Count > 0) return ParagraphState.Done;            // every rewrite applied, marked or resolved
        return ParagraphState.Read;
    }

    /// <summary>Text compared without its spacing and curly-quote differences.</summary>
    public static string Normal(string? s) =>
        Regex.Replace((s ?? "").Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"'), @"\s+", " ").Trim();

    private static HashSet<string> Words(string s) =>
        Regex.Matches(s.ToLowerInvariant(), @"[\p{L}\p{N}']+").Select(m => m.Value).ToHashSet();

    private static double Similarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        int shared = a.Count(b.Contains);
        return (double)shared / Math.Max(a.Count, b.Count);
    }
}
