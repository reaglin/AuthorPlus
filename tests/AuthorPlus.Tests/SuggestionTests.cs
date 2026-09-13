using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

public class SuggestionParserTests
{
    [Fact]
    public void Parses_numbered_suggestions_with_original_rewrite_and_why()
    {
        var text = "Here are my suggestions.\n\n" +
                   "SUGGESTION 1\nORIGINAL:\nThe message appeared on every broadcast television screen in the world simultaneously.\n" +
                   "REWRITE:\nEvery broadcast screen in the world showed the message at once.\nWHY:\nShorter; the adverb was doing the verb's work.\n\n" +
                   "### Suggestion 2\n**Original:** \"White text on a black screen.\"\n**Rewrite:** White on black.\n**Why:** Punchier.\n\n" +
                   "Suggestion 3\nORIGINAL:\nno rewrite follows\nWHY:\nskip me\n";
        var s = SuggestionParser.Parse(text);

        Assert.Equal(2, s.Count);
        Assert.Equal(1, s[0].Index);
        Assert.StartsWith("The message appeared", s[0].Original);
        Assert.Equal("Every broadcast screen in the world showed the message at once.", s[0].Rewrite);
        Assert.Equal("Shorter; the adverb was doing the verb's work.", s[0].Why);
        Assert.Equal(2, s[1].Index);
        Assert.Equal("White text on a black screen.", s[1].Original);   // quotes stripped
        Assert.Equal("White on black.", s[1].Rewrite);
        Assert.All(s, e => Assert.Equal(SuggestionStatus.Open, e.Status));
    }

    [Fact]
    public void Multi_line_passages_keep_their_line_breaks_and_empty_input_is_empty()
    {
        var text = "SUGGESTION 1\nORIGINAL:\nFirst line.\nSecond line.\nREWRITE:\nOne line.\nWHY:\nTighter.";
        var s = SuggestionParser.Parse(text);
        Assert.Equal("First line.\nSecond line.", s.Single().Original);
        Assert.Empty(SuggestionParser.Parse(""));
        Assert.Empty(SuggestionParser.Parse("No structure at all."));
    }
}

public class WordDiffTests
{
    [Fact]
    public void Marks_removed_and_added_words_and_joins_back_to_both_texts()
    {
        var before = "The message appeared on every screen in the world simultaneously.";
        var after  = "The message showed on every screen at once.";
        var d = WordDiff.Compute(before, after);

        Assert.Equal(before, string.Concat(WordDiff.Left(d).Select(p => p.Text)));
        Assert.Equal(after, string.Concat(WordDiff.Right(d).Select(p => p.Text)));
        Assert.Contains(d, p => p.Kind == DiffKind.Removed && p.Text.Contains("appeared"));
        Assert.Contains(d, p => p.Kind == DiffKind.Added && p.Text.Contains("showed"));
        Assert.Contains(d, p => p.Kind == DiffKind.Removed && p.Text.Contains("simultaneously"));
        Assert.Contains(d, p => p.Kind == DiffKind.Equal && p.Text.Contains("every screen"));
    }

    [Fact]
    public void Identical_text_is_all_equal_and_empty_sides_work()
    {
        Assert.All(WordDiff.Compute("same words here", "same words here"), p => Assert.Equal(DiffKind.Equal, p.Kind));
        Assert.All(WordDiff.Compute("", "new text"), p => Assert.Equal(DiffKind.Added, p.Kind));
        Assert.All(WordDiff.Compute("old text", ""), p => Assert.Equal(DiffKind.Removed, p.Kind));
    }
}

public class PassageLocatorTests
{
    private const string Chapter = "The message appeared on every broadcast television screen in the world simultaneously. It only lasted for ninety  seconds.\n\n" +
                                   "White text on a black screen. “Two have been removed. They will not return.”";

    [Fact]
    public void Finds_a_passage_despite_quote_style_and_whitespace_differences()
    {
        var span = PassageLocator.Find(Chapter, "It only lasted for ninety seconds.");
        Assert.NotNull(span);
        Assert.Equal("It only lasted for ninety  seconds.", Chapter.Substring(span!.Start, span.Length));

        var quoted = PassageLocator.Find(Chapter, "\"Two have been removed. They will not return.\"");
        Assert.NotNull(quoted);
        Assert.Equal("“Two have been removed. They will not return.”", Chapter.Substring(quoted!.Start, quoted.Length));
    }

    [Fact]
    public void Falls_back_to_the_opening_of_a_long_passage_and_returns_null_when_absent()
    {
        var long_ = "The message appeared on every broadcast television screen in the world simultaneously. It only lasted for ninety seconds and then something not in the text";
        var span = PassageLocator.Find(Chapter, long_);
        Assert.NotNull(span);
        Assert.StartsWith("The message appeared", Chapter.Substring(span!.Start, span.Length));
        Assert.Null(PassageLocator.Find(Chapter, "Nothing like this is in the chapter."));
        Assert.Null(PassageLocator.Find("", "x"));
    }
}

public class SuggestionPersistenceTests
{
    [Fact]
    public void Suggestion_entries_and_resolved_flags_round_trip()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("S");
        var ch = new Chapter();
        book.Chapters.Add(ch);
        var item = new Item { OwnerId = ch.Id, Kind = ItemKind.Suggestions, Title = "Suggestions · Pacing · 1", Resolved = true, ResolvedUtc = DateTime.UtcNow };
        item.Suggestions.Add(new SuggestionEntry { Index = 1, Original = "a", Rewrite = "b", Why = "c", Status = SuggestionStatus.Marked, AuthorRewrite = "mine" });
        book.Items.Add(item);
        store.Save(book);

        var back = store.Load(book.FolderPath).Items.Single();
        Assert.True(back.Resolved);
        var e = back.Suggestions.Single();
        Assert.Equal(SuggestionStatus.Marked, e.Status);
        Assert.Equal("mine", e.AuthorRewrite);
        Assert.Contains("\"Status\": \"Marked\"", File.ReadAllText(Directory.GetFiles(Path.Combine(book.FolderPath, "items"))[0]));
    }
}
