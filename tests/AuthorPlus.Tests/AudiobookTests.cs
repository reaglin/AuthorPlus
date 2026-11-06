using System.Buffers.Binary;
using System.Text;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Audio;

namespace AuthorPlus.Tests;

/// <summary>The audiobook and name pronunciations (plan phase 10).</summary>
public class PronunciationTests
{
    private static readonly List<Pronunciation> List = new()
    {
        new() { Word = "Olemelukwe", SayAs = "oh-leh-meh-LOO-kway", Ipa = "oʊlɛmɛˈluːkweɪ" },
        new() { Word = "Dalgo", SayAs = "DAL-go" },
        new() { Word = "Gull Point", SayAs = "gull point" },
        new() { Word = "Gull", SayAs = "GULL-ee" },
        new() { Word = "Mark", SayAs = "mahrk" }
    };

    [Fact]
    public void Respellings_replace_whole_words_longest_first_and_case_aware()
    {
        var s = Pronunciations.Apply("Dalgo's boat left Gull Point. A gull cried. Mark the mark. Dalgoan.", List, voiceTakesIpa: false);
        Assert.Equal("dal-go's boat left gull point. A gull cried. mahrk the mark. Dalgoan.", s.Text);   // "Gull" has a capital, so "a gull" is left alone
        Assert.Empty(s.Phonetics);
    }

    [Fact]
    public void Voices_that_take_phonetics_keep_the_word_and_get_its_IPA()
    {
        var s = Pronunciations.Apply("Olemelukwe met Dalgo.", List, voiceTakesIpa: true);
        Assert.Equal("Olemelukwe met dal-go.", s.Text);                         // no IPA for Dalgo: respelled
        Assert.Equal(("Olemelukwe", "oʊlɛmɛˈluːkweɪ"), s.Phonetics.Single());
        Assert.NotEqual(Pronunciations.Apply("Olemelukwe met Dalgo.", List, false).Key, s.Key);
    }

    [Fact]
    public void The_AIs_list_is_read_and_as_written_entries_are_left_out()
    {
        var parsed = Pronunciations.Parse("Word | Say it as | IPA | Why\nOlemelukwe | oh-leh-meh-LOO-kway | /oʊlɛmɛˈluːkweɪ/ | Igbo-like name\n- Nell | as written | - | common\nDalgo | DAL-go | ˈdælɡoʊ |");
        Assert.Equal(new[] { "Olemelukwe", "Dalgo" }, parsed.Select(p => p.Word));
        Assert.Equal("oʊlɛmɛˈluːkweɪ", parsed[0].Ipa);
        Assert.True(parsed.All(p => p.FromAi));
    }

    [Fact]
    public void Candidates_are_the_casts_names_and_the_books_capitalised_words_not_yet_listed()
    {
        var book = new Book();
        book.Characters.Add(new Character { Name = "Olemelukwe Dalgo", Aliases = "the Ferryman" });
        book.Pronunciations.Add(new Pronunciation { Word = "Dalgo", SayAs = "DAL-go" });
        var c = Pronunciations.Candidates(book, new[] { "Then Olemelukwe crossed to Kerrow Sound. Later Kerrow Sound was dark." });
        Assert.Contains("Olemelukwe", c);
        Assert.Contains("the Ferryman", c);
        Assert.Contains("Kerrow Sound", c);
        Assert.DoesNotContain("Dalgo", c);
    }

    [Fact]
    public void The_list_and_credits_round_trip()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-pron-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BookStore(root);
            var book = store.Create("Names", "A");
            book.Pronunciations.Add(List[0]);
            book.OpeningCredits = "Hello.";
            store.Save(book);
            var back = store.Load(book.FolderPath!);
            Assert.Equal("oʊlɛmɛˈluːkweɪ", back.Pronunciations.Single().Ipa);
            Assert.Equal("Hello.", back.OpeningCredits);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}

public class ChapterSoundTests
{
    private static PcmAudio Tone(int ms, short amp, int rate = 44100, int quietMs = 0)
    {
        var quiet = rate * quietMs / 1000;
        var n = rate * ms / 1000;
        var s = new short[quiet + n + quiet];
        for (int i = 0; i < n; i++) s[quiet + i] = (short)(amp * Math.Sin(2 * Math.PI * 220 * i / rate));
        return new PcmAudio(rate, s);
    }

    [Fact]
    public void A_wav_reads_back_as_written()
    {
        var a = Tone(50, 9000);
        var back = Wav.Read(Wav.Write(a));
        Assert.Equal(a.SampleRate, back.SampleRate);
        Assert.Equal(a.Samples, back.Samples);
        Assert.Throws<InvalidDataException>(() => Wav.Read(Encoding.ASCII.GetBytes("not a wav file at all")));
    }

    [Fact]
    public void Pieces_join_with_their_pauses_and_the_head_and_tail_silences()
    {
        var piece = Tone(500, 9000, quietMs: 200);                             // the voice's own silence is trimmed off
        var joined = ChapterSound.Join(44100, new[] { (piece, 0), (piece, ChapterSound.BetweenParagraphsMs) });
        double seconds = joined.Duration.TotalSeconds;
        double expected = (ChapterSound.HeadMs + ChapterSound.TailMs + ChapterSound.BetweenParagraphsMs) / 1000.0 + 2 * 0.5;
        Assert.InRange(seconds, expected - 0.01, expected + 0.03);              // + the breath kept at each end
        Assert.Throws<InvalidDataException>(() => ChapterSound.Join(24000, new[] { (piece, 0) }));
    }

    [Fact]
    public void Pauses_follow_what_comes_before()
    {
        var a = new ReadPart(0, 0, 5, "a", false, null);
        Assert.Equal(0, ChapterSound.PauseBefore(null, a, false));
        Assert.Equal(ChapterSound.InParagraphMs, ChapterSound.PauseBefore(a, a with { Start = 9 }, false));
        Assert.Equal(ChapterSound.BetweenParagraphsMs, ChapterSound.PauseBefore(a, a with { Paragraph = 1 }, false));
        Assert.Equal(ChapterSound.SceneBreakMs, ChapterSound.PauseBefore(a, a with { Paragraph = 3 }, true));
    }

    [Fact]
    public void Loudness_is_evened_to_the_target_with_peaks_held_under_the_ceiling()
    {
        var quiet = Tone(2000, 1200);
        var spike = (short[])quiet.Samples.Clone();
        for (int i = 30000; i < 30010; i++) spike[i] = 30000;                   // a click far louder than the speech
        var evened = ChapterSound.Even(new PcmAudio(44100, spike));
        Assert.InRange(ChapterSound.RmsDb(evened.Samples), -21.5, -18.5);
        Assert.True(ChapterSound.PeakDb(evened.Samples) <= -3.79);   // MP3 encoding adds a little: −3.8 leaves room under the stores' −3
        Assert.True(ChapterSound.Fingerprint("One", new[] { ("v", 1.0, "a") }) != ChapterSound.Fingerprint("One", new[] { ("v", 1.1, "a") }));
    }
}

public class Mp4BookTests
{
    private static byte[] Box(string type, params byte[][] body)
    {
        var len = 8 + body.Sum(b => b.Length);
        var r = new byte[len];
        BinaryPrimitives.WriteUInt32BigEndian(r, (uint)len);
        Encoding.Latin1.GetBytes(type).CopyTo(r, 4);
        int at = 8; foreach (var b in body) { b.CopyTo(r, at); at += b.Length; }
        return r;
    }
    private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }
    private static byte[] Full(string type, params byte[][] body) => Box(type, new[] { new byte[4] }.Concat(body).ToArray());

    /// <summary>A tiny MP4 like Windows writes: ftyp, mdat (the sound), moov — or the index first when <paramref name="indexFirst"/>.</summary>
    private static (string Path, byte[] Sound) Fake(bool indexFirst)
    {
        var sound = Encoding.ASCII.GetBytes("SOUND-BYTES-0123456789");
        var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("M4A "), U32(0));
        byte[] Moov(uint soundAt)
        {
            var mvhd = Full("mvhd", U32(0), U32(0), U32(1000), U32(65000), new byte[76], U32(2));   // 100-byte body ending in next_track_ID = 2
            var hdlr = Full("hdlr", U32(0), Encoding.ASCII.GetBytes("soun"), new byte[13]);
            var stco = Full("stco", U32(1), U32(soundAt));
            var trak = Box("trak", Full("tkhd", new byte[80]), Box("mdia", Full("mdhd", new byte[20]), hdlr, Box("minf", Box("stbl", stco))));
            return Box("moov", mvhd, trak);
        }
        var mdat = Box("mdat", sound);
        byte[] file;
        if (!indexFirst) file = ftyp.Concat(mdat).Concat(Moov((uint)(ftyp.Length + 8))).ToArray();
        else
        {
            var probe = Moov(0);
            file = ftyp.Concat(Moov((uint)(ftyp.Length + probe.Length + 8))).Concat(mdat).ToArray();
        }
        var path = Path.Combine(Path.GetTempPath(), "ap-mp4-" + Guid.NewGuid().ToString("N") + ".m4a");
        File.WriteAllBytes(path, file);
        return (path, sound);
    }

    private static uint SoundOffset(byte[] file)
    {
        var i = IndexOf(file, Encoding.ASCII.GetBytes("stco"));
        return BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(i + 4 + 4 + 4));
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++) if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Chapters_and_details_are_added_and_the_sound_stays_where_its_index_says(bool indexFirst)
    {
        var (path, sound) = Fake(indexFirst);
        var output = Path.ChangeExtension(path, ".m4b");
        try
        {
            var chapters = new[] { new ChapterMark("Opening credits", TimeSpan.Zero), new ChapterMark("1. The Cold Wick", TimeSpan.FromSeconds(12.5)), new ChapterMark("2. Ruth on the Slip", TimeSpan.FromSeconds(40)) };
            Mp4Book.Write(path, output, chapters, new AudiobookDetails { Title = "Gull Point", Author = "R. Eaglin", Narrator = "Synthesized voice", Cover = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 } });

            var (back, track, title) = Mp4Book.Inspect(output);
            Assert.Equal(chapters.Select(c => c.Title), back.Select(c => c.Title));
            Assert.Equal(TimeSpan.FromSeconds(12.5), back[1].Start);
            Assert.True(track);
            Assert.Equal("Gull Point", title);

            var file = File.ReadAllBytes(output);
            var at = SoundOffset(file);
            Assert.Equal(sound, file.AsSpan((int)at, sound.Length).ToArray());      // the sound's index still finds it
            Assert.True(IndexOf(file, Encoding.UTF8.GetBytes("2. Ruth on the Slip")) > 0);
        }
        finally { File.Delete(path); File.Delete(output); }
    }
}
