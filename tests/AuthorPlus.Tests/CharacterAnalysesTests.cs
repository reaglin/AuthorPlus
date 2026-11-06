using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

/// <summary>The AI's analyses of a character, in one chapter and across the book (plan phase 7).</summary>
public class CharacterAnalysesTests
{
    [Fact]
    public void A_chapter_analysis_splits_at_its_three_headings_however_they_are_written()
    {
        var reply = "**ROLE:**\n- Introduced in this chapter: she finds the wick.\n- The POV.\n\n" +
                    "## Description\n- \"her hand on the cold rail\"\n\n" +
                    "Motivations and actions: wants to know who put the light out.\n- climbs the stair";
        var parts = CharacterAnalyses.Sections(reply, CharacterAnalyses.ChapterHeadings);

        Assert.Equal(new[] { "ROLE", "DESCRIPTION", "MOTIVATIONS AND ACTIONS" }, parts.Select(p => p.Heading));
        Assert.StartsWith("- Introduced in this chapter", parts[0].Text);
        Assert.Equal("- \"her hand on the cold rail\"", parts[1].Text);
        Assert.Equal("wants to know who put the light out.\n- climbs the stair", parts[2].Text);   // text on the heading line belongs to it
    }

    [Fact]
    public void A_reply_without_the_headings_comes_back_whole()
    {
        var parts = CharacterAnalyses.Sections("She is not in this chapter.", CharacterAnalyses.ChapterHeadings);
        Assert.Equal(("", "She is not in this chapter."), parts.Single());
        Assert.Equal("", CharacterAnalyses.Part("She is not in this chapter.", CharacterAnalyses.ChapterHeadings, CharacterAnalyses.Role));
    }

    [Fact]
    public void A_word_inside_a_sentence_is_not_a_heading()
    {
        var parts = CharacterAnalyses.Sections("SUMMARY:\n- Her role in the story is the summary of the plot.\nSTORY ARC:\n- ch. 1 → 3", CharacterAnalyses.BookHeadings);
        Assert.Equal(2, parts.Count);
        Assert.Contains("role in the story", parts[0].Text);
    }

    private static (Book Book, Chapter One, Chapter Two, Chapter Three, Character Nell) Sample()
    {
        var book = new Book { Title = "Gull Point" };
        var one = new Chapter { Title = "The Cold Wick" };
        var two = new Chapter { Title = "Ruth on the Slip" };
        var three = new Chapter { Title = "Ink on His Cuffs" };
        book.Chapters.AddRange(new[] { one, two, three });
        var nell = new Character { Name = "Nell Carrow" };
        book.Characters.Add(nell);
        return (book, one, two, three, nell);
    }

    [Fact]
    public void The_latest_analysis_of_a_character_is_found_per_chapter_and_for_the_book()
    {
        var (book, one, two, _, nell) = Sample();
        var other = new Character { Name = "Tom" };
        var old = new Item { OwnerId = one.Id, Kind = ItemKind.CharacterInChapter, CharacterIds = { nell.Id }, Body = "old", CreatedUtc = DateTime.UtcNow.AddDays(-1) };
        var latest = new Item { OwnerId = one.Id, Kind = ItemKind.CharacterInChapter, CharacterIds = { nell.Id }, Body = "new" };
        var tom = new Item { OwnerId = two.Id, Kind = ItemKind.CharacterInChapter, CharacterIds = { other.Id }, Body = "tom" };
        var whole = new Item { OwnerId = nell.Id, Kind = ItemKind.CharacterInBook, Body = "book" };
        book.Items.AddRange(new[] { old, latest, tom, whole });

        Assert.Same(latest, CharacterAnalyses.InChapter(book, nell.Id, one.Id));
        Assert.Null(CharacterAnalyses.InChapter(book, nell.Id, two.Id));
        Assert.Same(whole, CharacterAnalyses.InBook(book, nell.Id));
        Assert.Null(CharacterAnalyses.InBook(book, other.Id));
        Assert.Equal(new[] { one }, CharacterAnalyses.ByChapter(book, nell.Id).Select(x => x.Chapter));
    }

    [Fact]
    public void The_book_analysis_draws_on_the_chapters_that_list_them_or_have_an_analysis()
    {
        var (book, one, two, three, nell) = Sample();
        two.CharacterIds.Add(nell.Id);
        book.Items.Add(new Item { OwnerId = three.Id, Kind = ItemKind.CharacterInChapter, CharacterIds = { nell.Id } });
        Assert.Equal(new[] { two, three }, CharacterAnalyses.ChaptersOf(book, nell));
    }

    [Fact]
    public void Where_they_first_appear_is_told_to_the_chapter_analysis()
    {
        var (book, one, two, three, nell) = Sample();
        Assert.StartsWith("No chapter lists them", CharacterAnalyses.Appearance(book, nell, two));

        two.CharacterIds.Add(nell.Id); three.CharacterIds.Add(nell.Id);
        Assert.Equal("This is the first chapter that lists them.", CharacterAnalyses.Appearance(book, nell, two));
        Assert.Contains("2. Ruth on the Slip", CharacterAnalyses.Appearance(book, nell, three));
        Assert.Equal("No earlier chapter lists them.", CharacterAnalyses.Appearance(book, nell, one));

        nell.ChapterRoles.Add(new CharacterChapterRole { ChapterId = three.Id, Role = CharacterRole.Introduced });
        Assert.StartsWith("The author has marked this chapter", CharacterAnalyses.Appearance(book, nell, three));
    }

    [Fact]
    public void Both_analyses_round_trip_through_the_book_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-charan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BookStore(root);
            var book = store.Create("Analyses", "A");
            var ch = new Chapter { Title = "One" };
            var nell = new Character { Name = "Nell" };
            book.Chapters.Add(ch); book.Characters.Add(nell);
            book.Items.Add(new Item { OwnerId = ch.Id, Kind = ItemKind.CharacterInChapter, CharacterIds = { nell.Id }, Body = "ROLE:\n- POV" });
            book.Items.Add(new Item { OwnerId = nell.Id, Kind = ItemKind.CharacterInBook, Body = "SUMMARY:\n- keeper's girl" });
            store.Save(book);

            var back = store.Load(book.FolderPath!);
            var backNell = back.Characters.Single();
            Assert.Equal("- POV", CharacterAnalyses.Part(CharacterAnalyses.InChapter(back, backNell.Id, back.Chapters[0].Id)!.Body, CharacterAnalyses.ChapterHeadings, "ROLE"));
            Assert.NotNull(CharacterAnalyses.InBook(back, backNell.Id));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
