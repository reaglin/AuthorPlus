using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Audio;
using AuthorPlus.Core.Services.Video;

namespace AuthorPlus.Tests;

/// <summary>The video of the audiobook: each paragraph on the screen as it is read (plan phase 11).</summary>
public class VideoTests
{
    private static PcmAudio Tone(int ms, int rate = 44100)
    {
        var s = new short[rate * ms / 1000];
        for (int i = 0; i < s.Length; i++) s[i] = (short)(9000 * Math.Sin(2 * Math.PI * 220 * i / rate));
        return new PcmAudio(rate, s);
    }

    [Fact]
    public void Joining_says_where_each_piece_is_heard()
    {
        var (audio, times) = ChapterSound.JoinTimed(44100, new[] { (Tone(500), 0), (Tone(1000), ChapterSound.BetweenParagraphsMs) });
        Assert.Equal(2, times.Count);
        Assert.InRange(times[0].Start, 0.99, 1.01);                                        // after the head's second of silence
        Assert.InRange(times[0].End - times[0].Start, 0.49, 0.52);
        Assert.InRange(times[1].Start - times[0].End, 0.64, 0.66);                          // the pause between paragraphs
        Assert.InRange(audio.Duration.TotalSeconds - times[1].End, 2.99, 3.01);             // the tail
        Assert.Equal(ChapterSound.Join(44100, new[] { (Tone(500), 0) }).Samples, ChapterSound.JoinTimed(44100, new[] { (Tone(500), 0) }).Audio.Samples);
    }

    [Fact]
    public void A_paragraph_is_on_the_screen_from_its_first_piece_until_the_next_one()
    {
        var pieces = new List<HeardPiece>
        {
            new(-1, 1, 3),          // the title
            new(0, 4, 6),           // paragraph 0, narration
            new(0, 6.3, 8),         // paragraph 0, a line of dialogue
            new(2, 9, 12)           // paragraph 2 (1 is a scene break, not read)
        };
        var paras = new[] { "First.", "* * *", "Second." };
        var screens = VideoScreens.ForTrack(pieces, paras, "Chapter 1\nThe Light", ScreenKind.Title, 15, _ => true);
        Assert.Equal(3, screens.Count);
        Assert.Equal(new VideoScreen(0, 3.7, ScreenKind.Title, "Chapter 1\nThe Light"), screens[0]);   // from the very start, over the head's silence
        Assert.Equal(new VideoScreen(3.7, 8.7, ScreenKind.Paragraph, "First."), screens[1]);           // a moment before its first word, through both pieces
        Assert.Equal(new VideoScreen(8.7, 15, ScreenKind.Paragraph, "Second."), screens[2]);         // to the end of the track
    }

    [Fact]
    public void A_long_paragraph_is_split_at_sentence_ends_and_each_page_gets_its_share()
    {
        Func<string, bool> fits = t => t.Length <= 31;
        var para = "One two three four. Five six seven eight. Nine ten.";
        var pages = VideoScreens.Pages(para, fits);
        Assert.Equal(new[] { "One two three four.", "Five six seven eight. Nine ten." }, pages);
        Assert.All(VideoScreens.Pages(new string('x', 5) + " " + string.Join(' ', Enumerable.Repeat("word", 30)) + ".", fits), p => Assert.True(fits(p)));

        var screens = VideoScreens.ForTrack(new List<HeardPiece> { new(0, 0, 10) }, new[] { para }, "", ScreenKind.Title, 12, fits);
        Assert.Equal(2, screens.Count);
        Assert.Equal(0, screens[0].Start);
        Assert.InRange(screens[0].End, 3.3, 3.6);                // 19 of 50 characters of 10 seconds, less the lead
        Assert.Equal(screens[0].End, screens[1].Start);
        Assert.Equal(12, screens[1].End);
    }

    [Fact]
    public void Times_are_estimated_from_the_length_when_they_were_not_kept()
    {
        var est = VideoScreens.Estimate(new[] { (-1, 10, 0), (0, 30, ChapterSound.AfterTitleMs) }, 1 + 1.3 + 3 + 8);
        Assert.InRange(est[0].Start, 0.99, 1.01);
        Assert.InRange(est[0].End, 2.99, 3.01);                  // 10 of 40 characters of 8 seconds
        Assert.InRange(est[1].Start, 4.29, 4.31);
        Assert.InRange(est[1].End, 10.29, 10.31);
    }

    [Fact]
    public void The_YouTube_description_lists_the_chapters_from_0_00()
    {
        var marks = new[] { new ChapterMark("Opening credits", TimeSpan.Zero), new ChapterMark("1. The Light", TimeSpan.FromSeconds(12)), new ChapterMark("2. The Ledger", TimeSpan.FromSeconds(3725)) };
        var text = VideoScreens.YouTubeDescription("The Keeper", "Ron Eaglin", "Charon", marks, TimeSpan.FromHours(2));
        Assert.Contains("0:00 Opening credits", text);
        Assert.Contains("0:12 1. The Light", text);
        Assert.Contains("1:02:05 2. The Ledger", text);
        Assert.DoesNotContain("YouTube makes chapters", text);
        Assert.Contains("YouTube makes chapters", VideoScreens.YouTubeDescription("T", "", "v", marks.Take(2).ToList(), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Nv12_has_brightness_then_colour_in_the_HD_standard()
    {
        // 2 × 2 pixels: white, black, pure red, pure blue (BGRA).
        var bgra = new byte[] { 255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 255, 255, 255, 0, 0, 255 };
        var nv = Nv12.FromBgra(bgra, 2, 2, 8);
        Assert.Equal(6, nv.Length);
        Assert.InRange((int)nv[0], 234, 236);                    // white → 235
        Assert.Equal(16, nv[1]);                                  // black → 16
        Assert.InRange((int)nv[2], 62, 64);                      // red's brightness (BT.709)
        Assert.InRange((int)nv[3], 31, 33);                      // blue's
        Assert.InRange((int)nv[4], 149, 151);                    // the average of the four (purple) leans blue …
        Assert.InRange((int)nv[5], 153, 155);                    // … and red
        Assert.Throws<ArgumentException>(() => Nv12.FromBgra(new byte[12], 3, 1, 12));
    }

    [Fact]
    public void The_videos_look_is_kept_with_the_book()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-video-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BookStore(root);
            var book = store.Create("Video", "A");
            Assert.Equal("Light", book.VideoLook);
            book.VideoLook = "Dark";
            store.Save(book);
            Assert.Equal("Dark", store.Load(book.FolderPath!).VideoLook);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Estimated_paragraph_starts_are_put_right_by_the_pauses_in_the_sound()
    {
        // 1 s of silence, 2 s of speech, 0.65 s pause, 3 s of speech, 1 s of silence.
        var rate = 8000;
        var s = new short[rate * 765 / 100];
        for (int i = rate; i < rate * 3; i++) s[i] = 5000;
        for (int i = rate * 365 / 100; i < rate * 665 / 100; i++) s[i] = 5000;
        var ends = ChapterSound.PauseEnds(new PcmAudio(rate, s));
        Assert.Equal(2, ends.Count);
        Assert.InRange(ends[0], 0.99, 1.01);
        Assert.InRange(ends[1], 3.64, 3.66);

        var est = new List<HeardPiece> { new(-1, 1, 3), new(0, 4.4, 7) };           // the estimate is 0.75 s late
        var snapped = VideoScreens.Snap(est, ends);
        Assert.Equal(1, snapped[0].Start);                                           // the first piece is not moved
        Assert.InRange(snapped[1].Start, 3.64, 3.66);
        Assert.Equal(4.4, VideoScreens.Snap(est, new[] { 9.0 })[1].Start);           // nothing near: left as estimated
    }
}
