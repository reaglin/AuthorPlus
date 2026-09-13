using System.Text.RegularExpressions;

namespace AuthorPlus.Core.Services.Style;

/// <summary>Numbers about a piece of prose, computed locally in milliseconds. No AI, no cost.</summary>
public sealed class StyleReport
{
    public int Words { get; init; }
    public int Sentences { get; init; }
    public int Paragraphs { get; init; }
    public double AverageSentenceLength { get; init; }
    public int LongestSentenceWords { get; init; }
    public string LongestSentence { get; init; } = "";
    /// <summary>Sentences by length: ≤10, 11–20, 21–30, 31–40, 41+ words.</summary>
    public IReadOnlyList<int> SentenceLengthBuckets { get; init; } = Array.Empty<int>();
    public int PassiveSentences { get; init; }
    public double PassivePercent { get; init; }
    public int Adverbs { get; init; }
    public double AdverbsPerHundredWords { get; init; }
    public double DialoguePercent { get; init; }
    public double FleschReadingEase { get; init; }
    public double AverageParagraphWords { get; init; }
    /// <summary>Most used content words (stop words removed), with counts.</summary>
    public IReadOnlyList<(string Word, int Count)> TopWords { get; init; } = Array.Empty<(string, int)>();
    /// <summary>Words that begin three or more sentences in a row somewhere ("He … He … He …").</summary>
    public IReadOnlyList<string> RepeatedSentenceStarts { get; init; } = Array.Empty<string>();

    public string FleschLabel => FleschReadingEase switch
    {
        >= 90 => "very easy", >= 80 => "easy", >= 70 => "fairly easy", >= 60 => "plain", >= 50 => "fairly hard", >= 30 => "hard", _ => "very hard"
    };
}

/// <summary>
/// Local prose statistics for a chapter: sentence lengths, passive voice (an auxiliary followed
/// by a past participle — a heuristic, so treat it as "worth a look" rather than a verdict),
/// adverbs ending in -ly, share of dialogue, Flesch reading ease, over-used words and repeated
/// sentence openings. Everything an editor eyeballs first; the AI read (template "style-read")
/// adds the judgement.
/// </summary>
public static class StyleMetrics
{
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?…][""'”’)]?)\s+(?=[""'“‘(]?[A-Z0-9])", RegexOptions.Compiled);
    private static readonly Regex WordRx = new(@"[A-Za-z][A-Za-z'’\-]*", RegexOptions.Compiled);
    private static readonly Regex PassiveRx = new(@"\b(?:am|is|are|was|were|be|been|being|get|got|gets|getting)\s+(?:\w+ly\s+)?(?:\w+ed|\w+en|built|bought|brought|caught|cut|done|felt|found|given|gone|heard|held|hit|kept|known|left|lost|made|met|put|read|said|seen|sent|set|shown|shut|sold|spent|taken|taught|thought|told|understood|won|written)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DialogueRx = new(@"[""“][^""”]{2,}[""”]", RegexOptions.Compiled);

    private static readonly HashSet<string> NotAdverbs = new(StringComparer.OrdinalIgnoreCase)
    { "only", "family", "early", "reply", "supply", "apply", "fly", "ally", "rally", "bully", "belly", "jelly", "holy", "ugly", "likely", "lonely", "lovely", "friendly", "daily", "italy", "july", "rely", "imply", "multiply", "assembly", "butterfly", "silly", "sly", "ply", "fully" };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","but","if","of","to","in","on","at","by","for","with","from","as","is","was","were","are","be","been","being","it","its","this","that","these","those",
        "he","she","they","them","his","her","their","him","we","you","i","me","my","our","your","not","no","so","than","then","there","here","had","has","have","do","did","does",
        "would","could","should","will","can","may","might","just","up","out","into","over","about","all","any","some","one","two","who","what","which","when","where","how","why",
        "s","t","d","ll","re","ve","m","said","like","back","down","more","also","very","through","after","before","again","still","even","off","own","other","because","while","around"
    };

    public static StyleReport Analyze(string text)
    {
        text = (text ?? "").Replace("\r\n", "\n");
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var flat = string.Join(" ", paragraphs.Select(p => p.Replace('\n', ' ')));
        var sentences = SentenceSplit.Split(flat).Select(s => s.Trim()).Where(s => WordRx.IsMatch(s)).ToList();
        var words = WordRx.Matches(flat).Select(m => m.Value).ToList();

        var lengths = sentences.Select(s => WordRx.Matches(s).Count).ToList();
        var buckets = new int[5];
        foreach (var n in lengths) buckets[n <= 10 ? 0 : n <= 20 ? 1 : n <= 30 ? 2 : n <= 40 ? 3 : 4]++;
        int longestIdx = lengths.Count == 0 ? -1 : lengths.IndexOf(lengths.Max());

        int passive = sentences.Count(PassiveRx.IsMatch);
        int adverbs = words.Count(w => w.Length > 4 && w.EndsWith("ly", StringComparison.OrdinalIgnoreCase) && !NotAdverbs.Contains(w));
        int dialogueChars = DialogueRx.Matches(flat).Sum(m => m.Length);

        double syllables = words.Sum(Syllables);
        double flesch = words.Count == 0 || sentences.Count == 0 ? 0
            : 206.835 - 1.015 * ((double)words.Count / sentences.Count) - 84.6 * (syllables / words.Count);

        var top = words.Select(w => w.Trim('\'', '’').ToLowerInvariant())
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .GroupBy(w => w).Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2).ThenBy(x => x.Key).Take(15).ToList();

        var repeated = new List<string>();
        string? prev = null; int run = 0;
        foreach (var s in sentences)
        {
            var first = WordRx.Match(s).Value.ToLowerInvariant();
            if (first == prev) run++; else { run = 1; prev = first; }
            if (run == 3 && !repeated.Contains(first)) repeated.Add(first);
        }

        return new StyleReport
        {
            Words = words.Count, Sentences = sentences.Count, Paragraphs = paragraphs.Count,
            AverageSentenceLength = sentences.Count == 0 ? 0 : Math.Round((double)words.Count / sentences.Count, 1),
            LongestSentenceWords = longestIdx < 0 ? 0 : lengths[longestIdx],
            LongestSentence = longestIdx < 0 ? "" : sentences[longestIdx],
            SentenceLengthBuckets = buckets,
            PassiveSentences = passive,
            PassivePercent = sentences.Count == 0 ? 0 : Math.Round(100.0 * passive / sentences.Count, 1),
            Adverbs = adverbs,
            AdverbsPerHundredWords = words.Count == 0 ? 0 : Math.Round(100.0 * adverbs / words.Count, 2),
            DialoguePercent = flat.Length == 0 ? 0 : Math.Round(100.0 * dialogueChars / flat.Length, 1),
            FleschReadingEase = Math.Round(flesch, 1),
            AverageParagraphWords = paragraphs.Count == 0 ? 0 : Math.Round((double)words.Count / paragraphs.Count, 1),
            TopWords = top,
            RepeatedSentenceStarts = repeated
        };
    }

    /// <summary>Syllable estimate: vowel groups, minus a silent trailing e, at least one.</summary>
    internal static int Syllables(string word)
    {
        var w = word.ToLowerInvariant().Trim('\'', '’');
        if (w.Length <= 3) return 1;
        int groups = Regex.Matches(w, "[aeiouy]+").Count;
        if (w.EndsWith('e') && !w.EndsWith("le") && groups > 1) groups--;                       // silent e: "phone"
        if (w.EndsWith("ed") && !w.EndsWith("ted") && !w.EndsWith("ded") && groups > 1) groups--; // "appeared", "watched" — not "needed"
        return Math.Max(1, groups);
    }

    /// <summary>The report as plain lines, for the AI style read and for copying.</summary>
    public static string Describe(StyleReport r)
    {
        var b = r.SentenceLengthBuckets;
        return string.Join("\n", new[]
        {
            $"Words: {r.Words:N0} in {r.Sentences:N0} sentences and {r.Paragraphs:N0} paragraphs (avg {r.AverageParagraphWords} words per paragraph)",
            $"Average sentence length: {r.AverageSentenceLength} words; longest {r.LongestSentenceWords} words",
            $"Sentence lengths: ≤10: {b.ElementAtOrDefault(0)}, 11–20: {b.ElementAtOrDefault(1)}, 21–30: {b.ElementAtOrDefault(2)}, 31–40: {b.ElementAtOrDefault(3)}, 41+: {b.ElementAtOrDefault(4)}",
            $"Passive-voice sentences (heuristic): {r.PassiveSentences} ({r.PassivePercent}%)",
            $"Adverbs in -ly: {r.Adverbs} ({r.AdverbsPerHundredWords} per 100 words)",
            $"Dialogue share: {r.DialoguePercent}% of the text",
            $"Flesch reading ease: {r.FleschReadingEase} ({r.FleschLabel})",
            $"Most used words: {string.Join(", ", r.TopWords.Take(10).Select(t => $"{t.Word} ×{t.Count}"))}",
            r.RepeatedSentenceStarts.Count > 0 ? $"Three or more sentences in a row start with: {string.Join(", ", r.RepeatedSentenceStarts)}" : "No runs of three sentences starting with the same word."
        });
    }
}
