using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

/// <summary>Dialogue for Listen mode and audiobooks (plan phase 9).</summary>
public class DialogueTests
{
    private static readonly string[] Paragraphs =
    {
        "She looked at his hands.",
        "\"You were up there,\" her father said when Nell came down. It was not a question. \"You saw it burning.\"",
        "“Yes,” she said.",
        "“Yes,” said Ruth, from the door. “I saw it too.”",
        "He was 6\" taller than her."
    };

    [Fact]
    public void A_paragraph_splits_at_its_quotation_marks()
    {
        var spans = Dialogue.Split(Paragraphs[1]);
        Assert.Equal(new[] { true, false, true }, spans.Select(s => s.IsQuote));
        Assert.Equal("\"You were up there,\"", spans[0].Text);
        Assert.Equal("\"You saw it burning.\"", spans[2].Text);
        Assert.Single(Dialogue.Split(Paragraphs[4]));                           // an inch mark is not a quotation
    }

    [Fact]
    public void A_speech_left_open_runs_to_the_end_of_the_paragraph()
    {
        var spans = Dialogue.Split("He said, “It was dark, and the wind");
        Assert.True(spans[^1].IsQuote);
        Assert.Equal("“It was dark, and the wind", spans[^1].Text);
    }

    [Fact]
    public void Lines_count_the_same_words_said_twice()
    {
        var lines = Dialogue.Lines(Paragraphs);
        Assert.Equal(new[] { "You were up there,", "You saw it burning.", "Yes,", "Yes,", "I saw it too." }, lines.Select(l => l.Key));
        Assert.Equal(new[] { 0, 0, 0, 1, 0 }, lines.Select(l => l.Occurrence));
    }

    [Fact]
    public void Speakers_are_found_by_words_and_occurrence_and_the_author_wins()
    {
        var nell = new Character { Name = "Nell Carrow" }; var ruth = new Character { Name = "Ruth Penhale" };
        var lines = Dialogue.Lines(Paragraphs);
        var item = new Item { Kind = ItemKind.ChapterSpeakers };
        Dialogue.SetSpeaker(item, lines[2], nell.Id, null, byAuthor: false);
        Dialogue.SetSpeaker(item, lines[3], nell.Id, null, byAuthor: true);
        Dialogue.SetSpeaker(item, lines[3], ruth.Id, null, byAuthor: false);       // the AI does not overwrite the author
        Assert.Equal(nell.Id, Dialogue.SpeakerOf(item, lines[2])!.CharacterId);
        Assert.Equal(nell.Id, Dialogue.SpeakerOf(item, lines[3])!.CharacterId);
        Assert.Null(Dialogue.SpeakerOf(item, lines[4]));
    }

    [Fact]
    public void A_speaker_stays_with_their_line_when_the_same_words_are_added_earlier()
    {
        var nell = Guid.NewGuid(); var ruth = Guid.NewGuid();
        var item = new Item { Kind = ItemKind.ChapterSpeakers };
        var lines = Dialogue.Lines(Paragraphs);
        Dialogue.SetSpeaker(item, lines[2], nell, null, byAuthor: true);      // “Yes,” she said.
        Dialogue.SetSpeaker(item, lines[3], ruth, null, byAuthor: true);      // “Yes,” said Ruth…

        var edited = new[] { "“Yes,” the boy said." }.Concat(Paragraphs).ToArray();   // a new "Yes," before both
        var again = Dialogue.Lines(edited);
        Assert.Null(Dialogue.SpeakerOf(item, again[0]));                    // the new line has no speaker
        Assert.Equal(nell, Dialogue.SpeakerOf(item, again[3])!.CharacterId);  // and the others keep theirs
        Assert.Equal(ruth, Dialogue.SpeakerOf(item, again[4])!.CharacterId);
    }

    [Fact]
    public void The_chapter_is_marked_and_the_answer_read_back()
    {
        var lines = Dialogue.Lines(Paragraphs);
        var marked = Dialogue.Marked(Paragraphs, lines);
        Assert.Contains("⟦1⟧\"You were up there,\"", marked);
        Assert.Contains("⟦5⟧“I saw it too.”", marked);

        var answer = Dialogue.ParseSpeakers("1 | Tom Carrow\n2 | Tom Carrow\n3 | Nell\n- 4: Ruth\n⟦5⟧ | ?");
        Assert.Equal("Tom Carrow", answer[1]);
        Assert.Equal("Ruth", answer[4]);
        Assert.True(Dialogue.IsUnknown(answer[5]));
        Assert.True(Dialogue.IsNarrator("Narrator"));
    }

    [Fact]
    public void A_name_matches_a_character_by_name_also_called_or_first_name()
    {
        var tom = new Character { Name = "Tom Carrow", Aliases = "Father" };
        var cast = new[] { tom, new Character { Name = "Nell Carrow" } };
        Assert.Same(tom, Dialogue.Match("tom carrow", cast));
        Assert.Same(tom, Dialogue.Match("Father", cast));
        Assert.Same(tom, Dialogue.Match("Tom", cast));
        Assert.Null(Dialogue.Match("the ferryman", cast));
    }

    [Fact]
    public void The_plan_reads_narration_and_each_line_in_its_speakers_voice()
    {
        var tom = Guid.NewGuid();
        var lines = Dialogue.Lines(Paragraphs);
        var item = new Item { Kind = ItemKind.ChapterSpeakers };
        Dialogue.SetSpeaker(item, lines[0], tom, null, false);
        var plan = Dialogue.Plan(Paragraphs.Take(2).ToList(), item);
        Assert.Equal(new[] { "She looked at his hands.", "You were up there,", "her father said when Nell came down. It was not a question.", "You saw it burning." },
                     plan.Select(p => p.Text));
        Assert.Equal(tom, plan[1].CharacterId);
        Assert.Null(plan[3].CharacterId);                                        // no one recorded: the narrator reads it
        Assert.Equal(Dialogue.AudioKey("v", 1, "x"), Dialogue.AudioKey("v", 1.0, "x"));
        Assert.NotEqual(Dialogue.AudioKey("v", 1, "x"), Dialogue.AudioKey("v", 1.1, "x"));
    }

    [Fact]
    public void Voices_and_speakers_round_trip_and_audio_is_never_versioned_or_exported()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-voices-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BookStore(root);
            var book = store.Create("Voices", "A");
            book.NarratorVoice = new VoiceChoice { Name = "en-US-Chirp3-HD-Charon", SpeakingRate = 0.95, Label = "Charon" };
            var tom = new Character { Name = "Tom", Voice = new VoiceChoice { Name = "en-US-Neural2-D" } };
            var ch = new Chapter { Title = "One" };
            book.Characters.Add(tom); book.Chapters.Add(ch);
            book.Items.Add(new Item { OwnerId = ch.Id, Kind = ItemKind.ChapterSpeakers, Lines = { new SpokenLine { Quote = "Yes,", Occurrence = 1, CharacterId = tom.Id, ByAuthor = true } } });
            store.Save(book);

            var back = store.Load(book.FolderPath!);
            Assert.Equal(0.95, back.NarratorVoice!.SpeakingRate);
            Assert.Equal("en-US-Neural2-D", back.Characters.Single().Voice!.Name);
            Assert.True(back.Items.Single(i => i.Kind == ItemKind.ChapterSpeakers).Lines.Single().ByAuthor);

            var repo = AuthorPlus.Core.Services.Versioning.BookRepository.Initialize(book.FolderPath!, new("A", "a@example.com"));
            Assert.Contains("audio/", File.ReadAllText(Path.Combine(book.FolderPath!, ".gitignore")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
