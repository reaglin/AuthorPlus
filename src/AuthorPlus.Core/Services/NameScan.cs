using System.Text.RegularExpressions;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>One name found in a piece of text: a character's (<see cref="CharacterId"/> set) or a possible new one.</summary>
public sealed record NameSpan(int Start, int Length, string Name, Guid? CharacterId);

/// <summary>
/// A capitalised name in a chapter that is not a character yet. <see cref="LikelyCharacterId"/>
/// is set when it is one word of a known character's name ("Nell" for "Nell Carrow"), so it can
/// be offered as another name for them rather than a new character.
/// </summary>
public sealed record NameCandidate(string Name, int Count, Guid? LikelyCharacterId);

/// <summary>
/// Character mode's name scan: where each character is named in a paragraph (by name or
/// "also called", whole words, longest first — the rules of <see cref="MentionFinder"/>), and the
/// capitalised names in a chapter that may be characters nobody has added yet.
/// </summary>
public static class NameScan
{
    /// <summary>
    /// Every place in <paramref name="text"/> a character is named, plus the places any of
    /// <paramref name="otherNames"/> appear, in text order. Longer names win, so "Lucy Dalgo" is
    /// one span and "Lucy" inside it is not counted again. A name written with a capital only
    /// matches with that capital ("Father" is not "her father"); one written in small letters
    /// matches either way ("the keeper's girl" also starts a sentence).
    /// </summary>
    public static IReadOnlyList<NameSpan> Spans(string text, IEnumerable<Character> characters, IEnumerable<string>? otherNames = null)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<NameSpan>();
        var names = new List<(string Name, Guid? Id)>();
        foreach (var c in characters)
            foreach (var n in MentionFinder.NamesOf(c)) names.Add((n, c.Id));
        foreach (var n in otherNames ?? Array.Empty<string>())
            if (n.Length > 1) names.Add((n, null));

        var working = text.ToCharArray();
        var spans = new List<NameSpan>();
        foreach (var (name, id) in names.OrderByDescending(n => n.Name.Length).ThenBy(n => n.Id is null))
        {
            var rx = new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (Match m in rx.Matches(new string(working)))
            {
                if (char.IsUpper(name[0]) && !char.IsUpper(m.Value[0])) continue;
                spans.Add(new NameSpan(m.Index, m.Length, name, id));
                for (int i = m.Index; i < m.Index + m.Length; i++) working[i] = ' ';
            }
        }
        return spans.OrderBy(s => s.Start).ToList();
    }

    // A run of capitalised words, with a title in front if there is one: "Mr. Abel", "Gull Point", "Nell".
    private static readonly Regex Capitalised = new(
        @"(?<![\p{L}\p{N}'’])(?:(?:Mr|Mrs|Ms|Dr|Miss|Captain|Father|Mother|Aunt|Uncle|Sir|Lady|Lord|Doctor|Professor)\.?\s+)?\p{Lu}[\p{Ll}]+(?:[-'’]\p{Lu}?[\p{Ll}]+)*(?:\s+\p{Lu}[\p{Ll}]+(?:[-'’]\p{Lu}?[\p{Ll}]+)*){0,2}",
        RegexOptions.CultureInvariant);

    /// <summary>Words that start sentences or are capitalised for other reasons; never names on their own.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","and","but","or","nor","so","yet","for","then","when","while","if","though","although","because","since","until","unless","once",
        "he","she","it","they","we","you","i","me","him","her","them","us","his","hers","its","their","our","your","my","mine","yours","theirs",
        "this","that","these","those","there","here","what","who","whom","whose","which","why","how","where","whatever","whoever",
        "yes","no","not","oh","ah","well","now","just","still","even","only","also","too","very","perhaps","maybe","sometimes","never","always","again",
        "after","before","as","at","in","on","of","from","to","with","by","into","onto","over","under","about","above","below","behind","beside","between",
        "through","across","around","against","along","among","without","within","inside","outside","up","down","out","off","near","past","toward","towards",
        "some","all","every","each","any","both","either","neither","none","nothing","something","everything","anything","someone","everyone","anyone","nobody",
        "one","two","three","four","five","six","seven","eight","nine","ten","first","second","third","last","next","other","another","more","most","many","much","few",
        "is","was","were","are","be","been","had","has","have","do","did","does","will","would","can","could","should","shall","may","might","must",
        "let","said","says","say","ask","asked","look","see","come","go","get","got","take","make","made","like","good","bad","old","new","little","big",
        "monday","tuesday","wednesday","thursday","friday","saturday","sunday",
        "january","february","march","april","june","july","august","september","october","november","december",
        "chapter","part","book","god","okay","ok","hello","hey","please","thanks","thank","sorry","no-one","whatever","whenever","wherever","instead","later","soon","today","tonight","tomorrow","yesterday"
    };

    private static readonly HashSet<string> Titles = new(StringComparer.OrdinalIgnoreCase)
    { "Mr","Mrs","Ms","Dr","Miss","Captain","Father","Mother","Aunt","Uncle","Sir","Lady","Lord","Doctor","Professor" };

    /// <summary>
    /// Capitalised names in a chapter's paragraphs that no character answers to. A name counts
    /// when it appears at least once in the middle of a sentence (where a capital means a name),
    /// or three times anywhere. Common words, titles alone, and what the characters already
    /// answer to are left out. Most mentioned first.
    /// </summary>
    public static IReadOnlyList<NameCandidate> Candidates(IEnumerable<string> paragraphs, IReadOnlyList<Character> characters, IEnumerable<string>? notNames = null)
    {
        var ignore = new HashSet<string>(notNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, (int All, int Mid)>(StringComparer.OrdinalIgnoreCase);
        var shown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in paragraphs)
        {
            // Blank out what the characters already answer to.
            var working = paragraph.ToCharArray();
            foreach (var s in Spans(paragraph, characters))
                for (int i = s.Start; i < s.Start + s.Length; i++) working[i] = ' ';
            var text = new string(working);

            foreach (Match m in Capitalised.Matches(text))
            {
                var words = m.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
                bool initial = SentenceInitial(text, m.Index);
                // Common words in front ("Then Nell", "The Keeper") are dropped; what is left is mid-sentence.
                while (words.Count > 0 && Common.Contains(Bare(words[0]))) { words.RemoveAt(0); initial = false; }
                while (words.Count > 0 && Common.Contains(Bare(words[^1]))) words.RemoveAt(words.Count - 1);
                if (words.Count == 0) continue;
                if (words.Count == 1 && Titles.Contains(Bare(words[0]))) continue;
                var name = string.Join(" ", words);
                if (name.EndsWith("'s") || name.EndsWith("’s")) name = name[..^2];
                if (name.Length < 2) continue;
                var c = counts.GetValueOrDefault(name);
                counts[name] = (c.All + 1, c.Mid + (initial ? 0 : 1));
                shown.TryAdd(name, name);
            }
        }

        var result = new List<NameCandidate>();
        foreach (var (name, c) in counts)
        {
            if (c.Mid == 0 && c.All < 3) continue;
            if (ignore.Contains(name)) continue;
            if (characters.Any(ch => MentionFinder.NamesOf(ch).Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))) continue;
            var likely = characters.FirstOrDefault(ch => !name.Contains(' ') &&
                ch.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.Equals(name, StringComparison.OrdinalIgnoreCase)));
            result.Add(new NameCandidate(shown[name], c.All, likely?.Id));
        }
        return result.OrderByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Bare(string word) => word.TrimEnd('.', ',').Replace("’s", "").Replace("'s", "");

    /// <summary>True when the word at <paramref name="index"/> starts its sentence (or the paragraph, or a quotation that does).</summary>
    private static bool SentenceInitial(string text, int index)
    {
        int i = index - 1;
        while (i >= 0 && (char.IsWhiteSpace(text[i]) || "\"“”'‘’(—–-".IndexOf(text[i]) >= 0)) i--;
        return i < 0 || ".!?…:".IndexOf(text[i]) >= 0;
    }
}
