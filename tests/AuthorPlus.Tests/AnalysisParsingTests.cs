using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

public class AnalysisSectionsTests
{
    [Fact]
    public void Splits_on_markdown_headings_keeping_an_unheaded_intro()
    {
        var body = "A strong chapter overall.\n\n## Pacing\nBrisk until the interview, then it sags.\n\n## Tension\n\nHigh.\n\n### Three changes:\n1. Cut the recap.\n2. Move the call.\n";
        var s = AnalysisSections.Parse(body);
        Assert.Equal(new[] { "", "Pacing", "Tension", "Three changes" }, s.Select(x => x.Heading));
        Assert.Equal("A strong chapter overall.", s[0].Body);
        Assert.Equal("Brisk until the interview, then it sags.", s[1].Body);
        Assert.Equal("High.", s[2].Body);
        Assert.StartsWith("1. Cut the recap.", s[3].Body);
    }

    [Fact]
    public void Plain_text_is_one_section_and_empty_is_none()
    {
        var s = AnalysisSections.Parse("Just prose, no headings.");
        Assert.Single(s);
        Assert.Equal("", s[0].Heading);
        Assert.Empty(AnalysisSections.Parse("  "));
        Assert.Equal(8, AnalysisSections.ChapterAspects.Count);
    }
}

public class ChapterExtractTests
{
    [Fact]
    public void Parses_characters_and_plotlines_with_new_flags_pov_and_notes()
    {
        var text = "CHARACTERS\n" +
                   "- Roland Ellison | known | POV | reads the message and calls his editor\n" +
                   "Lucy Dalgo | known | - | interviews the doorman\n" +
                   "2. Otay Olemelukwe | NEW | - | named as the second target\n" +
                   "Thomas Ellenberg | new | vanishes from his elevator\n" +
                   "(none of the others act)\n" +
                   "\nPLOTLINES\n" +
                   "The message | known | first broadcast, world reacts\n" +
                   "* The disappearances | new | Ellenberg and Olemelukwe vanish\n" +
                   "Roland Ellison | known | - | duplicate line should be ignored in this block? no — different block\n";
        var x = ChapterExtract.Parse(text);

        Assert.Equal(new[] { "Roland Ellison", "Lucy Dalgo", "Otay Olemelukwe", "Thomas Ellenberg" }, x.Characters.Select(c => c.Name));
        Assert.True(x.Characters[0].IsPov);
        Assert.False(x.Characters[0].IsNew);
        Assert.Equal("reads the message and calls his editor", x.Characters[0].Note);
        Assert.True(x.Characters[2].IsNew);
        Assert.Equal("named as the second target", x.Characters[2].Note);
        Assert.True(x.Characters[3].IsNew);
        Assert.Equal("vanishes from his elevator", x.Characters[3].Note);   // three columns, no POV column

        Assert.Equal(3, x.Plotlines.Count);
        Assert.False(x.Plotlines[0].IsNew);
        Assert.Equal("first broadcast, world reacts", x.Plotlines[0].Note);
        Assert.True(x.Plotlines[1].IsNew);
    }

    [Fact]
    public void Garbage_and_empty_input_give_nothing()
    {
        Assert.Empty(ChapterExtract.Parse("").Characters);
        var x = ChapterExtract.Parse("Sorry, I could not find any.\nCHARACTERS\nnone\nPLOTLINES\n(none)");
        Assert.Empty(x.Characters);
        Assert.Empty(x.Plotlines);
    }
}

public class ItemOwnershipTests
{
    [Fact]
    public void Items_can_own_items_and_removing_the_owner_cascades()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("Nested");
        var ch = new Chapter { Title = "One" };
        book.Chapters.Add(ch);
        var analysis = new Item { OwnerId = ch.Id, Kind = ItemKind.Analysis, Title = "Analysis" };
        var suggestion = new Item { OwnerId = analysis.Id, Kind = ItemKind.Suggestions, Title = "Suggestions · Pacing" };
        book.Items.AddRange(new[] { analysis, suggestion });
        store.Save(book);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(book.FolderPath, "items")).Length);   // the nested item survived the orphan prune

        book.RemoveItemTree(analysis);
        Assert.Empty(book.Items);
        store.Save(book);
        Assert.Empty(Directory.GetFiles(Path.Combine(book.FolderPath, "items")));
    }
}
