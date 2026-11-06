using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

/// <summary>Create mode's saved styles and its text; Plotlines mode's analyses (plan phase 8).</summary>
public class WritingStylesTests
{
    [Fact]
    public void Styles_are_kept_most_recent_first_each_once()
    {
        var path = Path.Combine(Path.GetTempPath(), "ap-styles-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var styles = new WritingStyles(path);
            Assert.Empty(styles.Load());
            styles.Remember("Full description of the challenges of old age");
            styles.Remember("Spare, short sentences");
            styles.Remember("   ");
            styles.Remember("full description of the challenges of old age");      // again, in other case: moves to the top
            Assert.Equal(new[] { "full description of the challenges of old age", "Spare, short sentences" }, new WritingStyles(path).Load());

            styles.Forget("Spare, short sentences");
            Assert.Single(styles.Load());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void An_unreadable_styles_file_is_an_empty_list()
    {
        var path = Path.Combine(Path.GetTempPath(), "ap-styles-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "not json");
        try { Assert.Empty(new WritingStyles(path).Load()); }
        finally { File.Delete(path); }
    }
}

public class CreatedTextTests
{
    [Fact]
    public void A_reply_becomes_paragraphs_at_blank_lines()
    {
        var reply = "Greg didn't simply wake; he negotiated\na truce with gravity.\n\nHe lay motionless for a long moment.";
        Assert.Equal(new[] { "Greg didn't simply wake; he negotiated a truce with gravity.", "He lay motionless for a long moment." }, CreatedText.Paragraphs(reply));
    }

    [Fact]
    public void Without_blank_lines_each_line_is_a_paragraph()
    {
        Assert.Equal(2, CreatedText.Paragraphs("One line.\nTwo line.").Count);
    }

    [Fact]
    public void A_preamble_fences_and_wrapping_quotes_are_taken_off()
    {
        Assert.Equal(new[] { "He woke." }, CreatedText.Paragraphs("Here is the passage:\n\"He woke.\""));
        Assert.Equal(new[] { "He woke.", "He rose." }, CreatedText.Paragraphs("```\nHe woke.\n\nHe rose.\n```"));
        Assert.Equal(new[] { "\"You saw it,\" he said." }, CreatedText.Paragraphs("\"You saw it,\" he said."));   // dialogue keeps its quotes
    }
}

public class PlotlineAnalysesTests
{
    private const string Reply =
        "ROLE:\n- Introduced: the chapter opens the question of who put out the lamp.\n\n" +
        "WHAT HAPPENS:\n- Nell finds the wick trimmed.\n\n" +
        "CHARACTERS:\n- Nell discovers; Tom lies.\n\n" +
        "KEY MOMENTS:\n- \"The wick was not burnt down. It had been trimmed\" — the clue.\n" +
        "- “There was soot on the thumb and forefinger” and \"You saw it burning.\" — the lie\n- Short.";

    [Fact]
    public void A_chapter_analysis_splits_into_its_four_parts()
    {
        var parts = CharacterAnalyses.Sections(Reply, PlotlineAnalyses.ChapterHeadings);
        Assert.Equal(new[] { "ROLE", "WHAT HAPPENS", "CHARACTERS", "KEY MOMENTS" }, parts.Select(p => p.Heading));
    }

    [Fact]
    public void Key_moments_are_the_quoted_words()
    {
        var moments = PlotlineAnalyses.MomentChoices(Reply);
        Assert.Equal(2, moments.Count);                                                       // "Short." quotes nothing and is too short
        Assert.Equal(new[] { "The wick was not burnt down. It had been trimmed" }, moments[0]);
        Assert.Contains("There was soot on the thumb and forefinger", moments[1]);            // two quotations in one point: each one
        Assert.Contains("You saw it burning.", moments[1]);
    }

    [Fact]
    public void A_quoted_passage_with_dialogue_in_it_is_kept_whole_first()
    {
        // As Claude wrote it: the dialogue's quotation marks escaped inside the quoted moment.
        var reply = """
            KEY MOMENTS:
            - "\"You were up there,\" her father said when Nell came down." — he answers before she asks
            """;
        var choices = PlotlineAnalyses.MomentChoices(reply).Single();
        Assert.Equal("\"You were up there,\" her father said when Nell came down.", choices[0]);
        Assert.Contains("You were up there,", choices);                                       // and the dialogue alone, if the whole is not found
    }

    [Fact]
    public void A_heading_word_starting_a_sentence_is_not_a_heading()
    {
        var parts = CharacterAnalyses.Sections("WHAT HAPPENS:\nCharacters gather on the slip.\nCHARACTERS:\n- Ruth", PlotlineAnalyses.ChapterHeadings);
        Assert.Equal("Characters gather on the slip.", parts.Single(p => p.Heading == "WHAT HAPPENS").Text);
    }

    [Fact]
    public void A_thread_added_to_a_chapter_is_introduced_there_unless_an_earlier_chapter_has_it()
    {
        var book = new Book();
        var one = new Chapter { Title = "One" }; var two = new Chapter { Title = "Two" }; var three = new Chapter { Title = "Three" };
        book.Chapters.AddRange(new[] { one, two, three });
        var lamp = new Plotline { Name = "Who put out the lamp" };
        book.Plotlines.Add(lamp);

        book.AddToChapter(lamp, two);
        Assert.Equal(PlotlineRole.Introduced, lamp.RoleIn(two.Id));
        Assert.Contains(lamp.Id, two.PlotlineIds);
        book.AddToChapter(lamp, three);
        Assert.Equal(PlotlineRole.Continuing, lamp.RoleIn(three.Id));
        Assert.Equal(PlotlineStatus.Active, lamp.Status);
        Assert.Equal(new[] { lamp }, PlotlineAnalyses.In(book, three));
        Assert.Equal(new[] { two, three }, PlotlineAnalyses.ChaptersOf(book, lamp));
    }

    [Fact]
    public void The_latest_analyses_are_found_and_a_characters_story_round_trips()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-plot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BookStore(root);
            var book = store.Create("Plots", "A");
            var ch = new Chapter { Title = "One" };
            var nell = new Character { Name = "Nell" };
            var story = new Plotline { Name = "Nell's story", CharacterId = nell.Id };
            book.Chapters.Add(ch); book.Characters.Add(nell); book.Plotlines.Add(story);
            book.Items.Add(new Item { OwnerId = ch.Id, Kind = ItemKind.PlotlineInChapter, PlotlineIds = { story.Id }, Body = "old", CreatedUtc = DateTime.UtcNow.AddHours(-1) });
            book.Items.Add(new Item { OwnerId = ch.Id, Kind = ItemKind.PlotlineInChapter, PlotlineIds = { story.Id }, Body = Reply });
            book.Items.Add(new Item { OwnerId = story.Id, Kind = ItemKind.PlotlineInBook, Body = "SUMMARY:\n- hers" });
            store.Save(book);

            var back = store.Load(book.FolderPath!);
            var p = back.Plotlines.Single();
            Assert.Equal(back.Characters.Single().Id, p.CharacterId);
            Assert.Equal(Reply, PlotlineAnalyses.InChapter(back, p.Id, back.Chapters[0].Id)!.Body);
            Assert.NotNull(PlotlineAnalyses.InBook(back, p.Id));
            Assert.Single(PlotlineAnalyses.ByChapter(back, p.Id));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
