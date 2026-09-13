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
        Assert.Equal(new[] { "Cut the recap.", "Move the call." }, s[3].Points);
    }

    [Fact]
    public void Points_split_on_bullets_or_numbers_with_continuation_lines_else_on_blank_lines()
    {
        var bulleted = "1. The opening runs long: \"the message appeared\" is\n   repeated three times.\n2) Second point.\n- third point\n* fourth";
        Assert.Equal(new[] { "The opening runs long: \"the message appeared\" is repeated three times.", "Second point.", "third point", "fourth" }, AnalysisSections.Points(bulleted));

        var prose = "First paragraph of notes.\n\nSecond paragraph.\n\n\nThird.";
        Assert.Equal(new[] { "First paragraph of notes.", "Second paragraph.", "Third." }, AnalysisSections.Points(prose));
        Assert.Empty(AnalysisSections.Points(" "));
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
    public void Parses_characters_and_plotlines_with_new_flags_pov_kind_and_notes()
    {
        var text = "CHARACTERS\n" +
                   "- Roland Ellison | known | POV | reads the message and calls his editor\n" +
                   "Lucy Dalgo | known | - | interviews the doorman\n" +
                   "2. Otay Olemelukwe | NEW | - | named as the second target\n" +
                   "Thomas Ellenberg | new | vanishes from his elevator\n" +
                   "(none of the others act)\n" +
                   "\nPLOTLINES\n" +
                   "The mystery of the message | new | primary | the broadcast and the first two disappearances\n" +
                   "* Lucy's investigation | new | subplot | she starts asking questions\n" +
                   "Roland's career | known | his editor gives him the story\n";
        var x = ChapterExtract.Parse(text);

        Assert.Equal(new[] { "Roland Ellison", "Lucy Dalgo", "Otay Olemelukwe", "Thomas Ellenberg" }, x.Characters.Select(c => c.Name));
        Assert.True(x.Characters[0].IsPov);
        Assert.False(x.Characters[0].IsNew);
        Assert.Equal("reads the message and calls his editor", x.Characters[0].Note);
        Assert.True(x.Characters[2].IsNew);
        Assert.Equal("vanishes from his elevator", x.Characters[3].Note);   // three columns, no POV column

        Assert.Equal(3, x.Plotlines.Count);
        Assert.Equal("primary", x.Plotlines[0].Kind);
        Assert.True(x.Plotlines[0].IsPrimary);
        Assert.True(x.Plotlines[0].IsNew);
        Assert.Equal("the broadcast and the first two disappearances", x.Plotlines[0].Note);
        Assert.Equal("subplot", x.Plotlines[1].Kind);
        Assert.Equal("", x.Plotlines[2].Kind);                                 // no kind column
        Assert.Equal("his editor gives him the story", x.Plotlines[2].Note);
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

public class PlotlineFinderTests
{
    [Fact]
    public void Parses_name_new_kind_chapters_and_description()
    {
        var text = "PLOTLINES\n" +
                   "1. The mystery of the message | new | primary | chapters: 1, 2, 3, 10 | who sent it and why\n" +
                   "- Lucy's investigation | new | subplot | 4, 12 | the detective's off-the-books case\n" +
                   "Roland's career | known | secondary | ch. 3, 11, 16 | the articles and what they cost him\n" +
                   "(none further)";
        var p = PlotlineFinder.Parse(text);
        Assert.Equal(3, p.Count);
        Assert.Equal("The mystery of the message", p[0].Name);
        Assert.True(p[0].IsNew); Assert.Equal("primary", p[0].Kind);
        Assert.Equal(new[] { 1, 2, 3, 10 }, p[0].Chapters);
        Assert.Equal("who sent it and why", p[0].Note);
        Assert.Equal(new[] { 4, 12 }, p[1].Chapters);
        Assert.Equal("subplot", p[1].Kind);
        Assert.False(p[2].IsNew);
        Assert.Equal(new[] { 3, 11, 16 }, p[2].Chapters);
        Assert.Equal("the articles and what they cost him", p[2].Note);
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

    [Fact]
    public void Plotline_kind_round_trips_as_a_name()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("Kinds");
        book.Plotlines.Add(new Plotline { Name = "Spine", Kind = PlotlineKind.Primary });
        store.Save(book);
        Assert.Contains("\"Kind\": \"Primary\"", File.ReadAllText(Directory.GetFiles(Path.Combine(book.FolderPath, "plotlines"))[0]));
        Assert.Equal(PlotlineKind.Primary, store.Load(book.FolderPath).Plotlines[0].Kind);
    }
}
