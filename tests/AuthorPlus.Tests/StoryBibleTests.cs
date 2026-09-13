using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

public class MentionFinderTests
{
    [Fact]
    public void Finds_whole_word_names_and_aliases_case_insensitively_longest_first()
    {
        var one   = new Character { Name = "One" };
        var lucy  = new Character { Name = "Lucy Dalgo", Aliases = "Lucy\nDetective Dalgo\n" };
        var rol   = new Character { Name = "Roland", Aliases = "  " };
        var text  = "Lucy Dalgo read the article. Someone had done it. Detective Dalgo called Roland; lucy waited. ONE watched. Roland's phone rang.";

        var found = MentionFinder.Find(text, new[] { one, lucy, rol });

        var l = found.Single(m => m.CharacterId == lucy.Id);
        Assert.Equal(3, l.Count);                    // "Lucy Dalgo", "Detective Dalgo", "lucy" — not "Lucy" inside "Lucy Dalgo" twice
        Assert.Equal("Lucy Dalgo", l.MatchedName);
        Assert.Equal(0, l.FirstIndex);
        var r = found.Single(m => m.CharacterId == rol.Id);
        Assert.Equal(2, r.Count);                    // "Roland" and "Roland's"
        var o = found.Single(m => m.CharacterId == one.Id);
        Assert.Equal(1, o.Count);                    // "ONE", not "Someone" / "done"
        Assert.Equal(lucy.Id, found[0].CharacterId); // most mentioned first
    }

    [Fact]
    public void Empty_text_or_no_characters_gives_nothing()
    {
        Assert.Empty(MentionFinder.Find("", new[] { new Character { Name = "X" } }));
        Assert.Empty(MentionFinder.Find("some text", Array.Empty<Character>()));
        Assert.Empty(MentionFinder.Find("Nobody here", new[] { new Character { Name = "Q" } }));   // one-letter names are ignored
    }
}

public class ConsistencyTests
{
    private static (Book book, Chapter c1, Chapter c2, Chapter c3, Character hero, Character villain, Plotline main) Fixture()
    {
        var book = new Book();
        var hero = new Character { Name = "Roland" };
        var villain = new Character { Name = "Edmund", Aliases = "Blackwood" };
        book.Characters.AddRange(new[] { hero, villain });
        var c1 = new Chapter { Title = "One" };
        var c2 = new Chapter { Title = "Two" };
        var c3 = new Chapter { Title = "Three" };
        book.Chapters.AddRange(new[] { c1, c2, c3 });
        var main = new Plotline { Name = "Main", Status = PlotlineStatus.Active, ChapterIds = { c1.Id, c2.Id } };
        book.Plotlines.Add(main);
        return (book, c1, c2, c3, hero, villain, main);
    }

    [Fact]
    public void Clean_book_yields_only_the_summary_note_and_the_unresolved_active_plotline()
    {
        var (book, c1, c2, _, hero, _, _) = Fixture();
        c1.CharacterIds.Add(hero.Id); c1.PovCharacterId = hero.Id;
        c2.CharacterIds.Add(hero.Id);
        book.SetSummary(c1, "s"); book.SetSummary(c2, "s");

        var f = Consistency.Check(book);

        Assert.Equal(new[] { "unresolved", "summary" }, f.Select(x => x.Rule));
        Assert.Equal(Severity.Warning, f[0].Severity);
        Assert.Contains("1 of 3", f[1].Message);
    }

    [Fact]
    public void Dangling_references_are_errors_pointing_at_the_owner()
    {
        var (book, c1, _, _, _, _, main) = Fixture();
        c1.CharacterIds.Add(Guid.NewGuid());
        c1.PovCharacterId = Guid.NewGuid();
        var ev = new TimelineEvent { Title = "Vanishing", ChapterIds = { Guid.NewGuid() } };
        book.Timeline.Add(ev);
        main.Convergences.Add(new PlotlineConvergence { OtherPlotlineId = Guid.NewGuid() });

        var f = Consistency.Check(book).Where(x => x.Rule == "dangling").ToList();

        Assert.Equal(4, f.Count);
        Assert.All(f, x => Assert.Equal(Severity.Error, x.Severity));
        Assert.Equal(2, f.Count(x => x.TargetId == c1.Id));
        Assert.Contains(f, x => x.TargetId == ev.Id);
        Assert.Contains(f, x => x.TargetId == main.Id);
    }

    [Fact]
    public void Pov_must_be_present_and_plotlines_need_chapters_and_paired_convergences()
    {
        var (book, c1, _, _, hero, _, main) = Fixture();
        c1.PovCharacterId = hero.Id;                         // present list empty
        var side = new Plotline { Name = "Side", Status = PlotlineStatus.Resolved };   // resolved, no chapters
        book.Plotlines.Add(side);
        main.Convergences.Add(new PlotlineConvergence { OtherPlotlineId = side.Id, ChapterId = c1.Id });   // one-sided

        var f = Consistency.Check(book);

        Assert.Contains(f, x => x.Rule == "pov" && x.TargetId == c1.Id);
        Assert.Contains(f, x => x.Rule == "orphan-plotline" && x.TargetId == side.Id);
        Assert.Contains(f, x => x.Rule == "convergence" && x.TargetId == side.Id);
        Assert.DoesNotContain(f, x => x.Rule == "unresolved" && x.TargetId == side.Id);
    }

    [Fact]
    public void Events_told_out_of_story_order_are_information()
    {
        var (book, c1, c2, c3, _, _, _) = Fixture();
        book.Timeline.Add(new TimelineEvent { Order = 1, Title = "Childhood", ChapterIds = { c3.Id } });   // flashback in ch 3
        book.Timeline.Add(new TimelineEvent { Order = 2, Title = "The message", ChapterIds = { c1.Id } });
        book.Timeline.Add(new TimelineEvent { Order = 3, Title = "The chase", ChapterIds = { c2.Id } });

        var f = Consistency.Check(book).Where(x => x.Rule == "story-order").ToList();

        var only = Assert.Single(f);
        Assert.Equal(Severity.Info, only.Severity);
        Assert.Contains("\"The message\" comes after \"Childhood\"", only.Message);
        Assert.Contains("chapter 1 vs 3", only.Message);
    }

    [Fact]
    public void Mentions_before_introduction_and_unlinked_characters_use_the_chapter_text()
    {
        var (book, c1, c2, c3, hero, villain, _) = Fixture();
        c2.CharacterIds.Add(hero.Id);                        // hero "introduced" in chapter Two
        var texts = new Dictionary<Guid, string>
        {
            [c1.Id] = "Roland was already here.",            // named before intro
            [c2.Id] = "Roland again. Blackwood watched.",    // villain never linked anywhere
            [c3.Id] = "Nothing."
        };

        var f = Consistency.Check(book, ch => texts[ch.Id]);

        Assert.Contains(f, x => x.Rule == "before-intro" && x.TargetId == c1.Id && x.Message.Contains("Roland"));
        Assert.Contains(f, x => x.Rule == "unlinked" && x.TargetId == c2.Id && x.Message.Contains("Edmund"));
        Assert.DoesNotContain(f, x => x.Rule == "before-intro" && x.TargetId == c2.Id);
    }
}
