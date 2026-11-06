using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

/// <summary>The Core behind the editor's Analysis and Character modes (plan phase 6).</summary>
public class ParagraphReadsTests
{
    private const string Reply =
        "READ:\n- The paragraph moves Nell from the stair to the lamp room.\n- \"very dark\" says what the next line shows.\n\n" +
        "SUGGESTION 1\nORIGINAL:\nIt was very dark.\nREWRITE:\nThe dark had weight.\nWHY:\nShows it.\n";

    [Fact]
    public void Read_part_is_the_text_before_the_first_suggestion_without_its_label()
    {
        var read = ParagraphReads.ReadPart(Reply);
        Assert.StartsWith("- The paragraph moves Nell", read);
        Assert.DoesNotContain("SUGGESTION", read);
        Assert.DoesNotContain("READ", read);
        Assert.Single(SuggestionParser.Parse(Reply));             // the rewrites still parse from the same reply
    }

    [Fact]
    public void Read_part_of_a_reply_with_no_suggestions_is_all_of_it()
    {
        Assert.Equal("It already does its job.", ParagraphReads.ReadPart("**READ:**\nIt already does its job."));
    }

    private static readonly string[] Paragraphs =
    {
        "Nell climbed the stair with the lamp oil.",
        "It was very dark, and the wind was up.",
        "Tom waited at the top, saying nothing at all.",
        "It was very dark, and the wind was up."
    };

    [Fact]
    public void A_paragraph_is_found_by_its_text_nearest_where_it_was()
    {
        Assert.Equal((1, true), ParagraphReads.Match(Paragraphs, "It was very dark, and the wind was up.", 0));
        Assert.Equal((3, true), ParagraphReads.Match(Paragraphs, "It was  very dark, and the wind was up. ", 3));   // spacing does not matter
    }

    [Fact]
    public void A_changed_paragraph_is_still_found_when_most_of_its_words_remain()
    {
        var (index, exact) = ParagraphReads.Match(Paragraphs, "Tom waited at the top of the stair, saying nothing.", 2);
        Assert.Equal(2, index);
        Assert.False(exact);
    }

    [Fact]
    public void A_paragraph_that_is_gone_is_not_matched()
    {
        Assert.Equal(-1, ParagraphReads.Match(Paragraphs, "Something else entirely happened in the harbour.", 1).Index);
    }

    [Fact]
    public void State_follows_the_reads_and_their_rewrites()
    {
        Assert.Equal(ParagraphState.None, ParagraphReads.StateOf(Array.Empty<Item>()));
        var read = new Item { Kind = ItemKind.ParagraphAnalysis };
        Assert.Equal(ParagraphState.Read, ParagraphReads.StateOf(new[] { read }));
        read.Suggestions.Add(new SuggestionEntry { Original = "a", Rewrite = "b" });
        Assert.Equal(ParagraphState.Open, ParagraphReads.StateOf(new[] { read }));
        read.Suggestions[0].Status = SuggestionStatus.Applied;
        Assert.Equal(ParagraphState.Done, ParagraphReads.StateOf(new[] { read }));
        read.Suggestions[0].Status = SuggestionStatus.Open;
        read.Resolved = true;
        Assert.Equal(ParagraphState.Done, ParagraphReads.StateOf(new[] { read }));

        // A newer read with rewrites waiting is not hidden by an older one marked dealt with.
        var newer = new Item { Kind = ItemKind.ParagraphAnalysis };
        newer.Suggestions.Add(new SuggestionEntry { Original = "c", Rewrite = "d" });
        Assert.Equal(ParagraphState.Open, ParagraphReads.StateOf(new[] { newer, read }));
    }

    [Fact]
    public void A_paragraph_read_round_trips_through_the_book_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-modes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BookStore(root);
            var book = store.Create("Modes", "A");
            var ch = new Chapter { Title = "One" };
            book.Chapters.Add(ch);
            var item = new Item { OwnerId = ch.Id, Kind = ItemKind.ParagraphAnalysis, Passage = Paragraphs[1], ParagraphIndex = 1, Body = Reply };
            item.Suggestions = SuggestionParser.Parse(Reply);
            book.Items.Add(item);
            store.Save(book);

            var back = store.Load(book.FolderPath!).Items.Single(i => i.Kind == ItemKind.ParagraphAnalysis);
            Assert.Equal(Paragraphs[1], back.Passage);
            Assert.Equal(1, back.ParagraphIndex);
            Assert.Single(back.Suggestions);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}

public class NameScanTests
{
    private static readonly Character Nell = new() { Name = "Nell Carrow", Aliases = "the keeper's girl" };
    private static readonly Character Tom  = new() { Name = "Tom", Aliases = "Father" };
    private static readonly Character Lucy = new() { Name = "Lucy Dalgo", Aliases = "Lucy" };

    [Fact]
    public void Spans_mark_each_character_name_and_alias_in_text_order()
    {
        var text = "Nell Carrow saw Tom. Tom did not see the keeper's girl, and Tomas laughed. The keeper's girl ran to her father. Father!";
        var spans = NameScan.Spans(text, new[] { Nell, Tom });
        Assert.Equal(new[] { "Nell Carrow", "Tom", "Tom", "the keeper's girl", "The keeper's girl", "Father" }, spans.Select(s => text.Substring(s.Start, s.Length)));
        Assert.Equal(Tom.Id, spans[1].CharacterId);
        Assert.Equal(Nell.Id, spans[3].CharacterId);              // the alias counts as Nell; "Tomas" is not Tom
        Assert.Equal(Tom.Id, spans[5].CharacterId);               // "Father" with its capital is Tom; "her father" is not
    }

    [Fact]
    public void The_longer_name_wins_over_a_shorter_one_inside_it()
    {
        var spans = NameScan.Spans("Lucy Dalgo came in. Lucy sat.", new[] { Lucy });
        Assert.Equal(new[] { 10, 4 }, spans.Select(s => s.Length));
    }

    [Fact]
    public void Other_names_are_marked_without_a_character()
    {
        var spans = NameScan.Spans("Mr. Abel waited for Tom.", new[] { Tom }, new[] { "Mr. Abel" });
        Assert.Equal(2, spans.Count);
        Assert.Null(spans[0].CharacterId);
        Assert.Equal(Tom.Id, spans[1].CharacterId);
    }

    [Fact]
    public void Candidates_are_names_used_mid_sentence_that_no_character_answers_to()
    {
        var paragraphs = new[]
        {
            "Then Nell climbed to the lamp room where Mr. Abel kept the logbook.",
            "The wind rose over Gull Point. Tom said nothing to Mr. Abel.",
            "Darkness came early. Monday was the worst day."
        };
        var found = NameScan.Candidates(paragraphs, new[] { Nell, Tom });
        var names = found.Select(c => c.Name).ToList();

        Assert.Contains("Mr. Abel", names);
        Assert.Contains("Gull Point", names);
        Assert.DoesNotContain("Tom", names);                      // already a character
        Assert.DoesNotContain("Darkness", names);                 // only ever starts a sentence, and only once
        Assert.DoesNotContain("Monday", names);                   // a common word
        Assert.DoesNotContain("Then", names);
        Assert.Equal(2, found.Single(c => c.Name == "Mr. Abel").Count);

        var nell = found.Single(c => c.Name == "Nell");           // part of Nell Carrow's name: offered as another name for her
        Assert.Equal(Nell.Id, nell.LikelyCharacterId);

        // A name the author said is not a character is not offered again.
        Assert.DoesNotContain("Gull Point", NameScan.Candidates(paragraphs, new[] { Nell, Tom }, new[] { "gull point" }).Select(c => c.Name));
    }

    [Fact]
    public void Names_the_author_set_aside_are_kept_with_the_book()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-notnames-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BookStore(root);
            var book = store.Create("Names", "A");
            book.NotNames.Add("Gull Point");
            store.Save(book);
            Assert.Equal(new[] { "Gull Point" }, store.Load(book.FolderPath!).NotNames);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
