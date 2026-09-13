using System.Text.RegularExpressions;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>How often a character is named in one piece of text, and where first.</summary>
public sealed record Mention(Guid CharacterId, int Count, int FirstIndex, string MatchedName);

/// <summary>
/// Finds characters by name or alias in prose: whole words, case-insensitive, longest names
/// first so "Lucy Dalgo" wins over "Lucy" and a character called "One" is not found inside
/// "someone". Aliases are one per line on <see cref="Character.Aliases"/>.
/// </summary>
public static class MentionFinder
{
    /// <summary>All the names a character answers to, longest first, blanks removed.</summary>
    public static IReadOnlyList<string> NamesOf(Character c) =>
        new[] { c.Name }.Concat(c.Aliases.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(n => n.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(n => n.Length)
            .ToList();

    /// <summary>Mentions of every character in <paramref name="text"/>, most mentioned first; characters with none are omitted.</summary>
    public static IReadOnlyList<Mention> Find(string text, IEnumerable<Character> characters)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<Mention>();
        var result = new List<Mention>();
        foreach (var c in characters)
        {
            int count = 0, first = int.MaxValue; string matched = "";
            var working = text.ToCharArray();                 // longer names are blanked out so shorter ones cannot re-match inside them
            foreach (var name in NamesOf(c))
            {
                var rx = new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var matches = rx.Matches(new string(working));
                if (matches.Count == 0) continue;
                count += matches.Count;
                if (matches[0].Index < first) { first = matches[0].Index; matched = name; }
                foreach (Match m in matches)
                    for (int i = m.Index; i < m.Index + m.Length; i++) working[i] = ' ';
            }
            if (count > 0) result.Add(new Mention(c.Id, count, first, matched));
        }
        return result.OrderByDescending(m => m.Count).ThenBy(m => m.FirstIndex).ToList();
    }

    /// <summary>Per chapter (reading order), which characters are named in its text. <paramref name="textOf"/> loads the prose.</summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<Mention>> FindInBook(Book book, Func<Chapter, string> textOf)
    {
        var map = new Dictionary<Guid, IReadOnlyList<Mention>>();
        foreach (var ch in book.Chapters)
            map[ch.Id] = Find(textOf(ch), book.Characters);
        return map;
    }
}
