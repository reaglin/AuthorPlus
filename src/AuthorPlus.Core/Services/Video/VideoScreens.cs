using System.Text;
using System.Text.RegularExpressions;
using AuthorPlus.Core.Services.Audio;

namespace AuthorPlus.Core.Services.Video;

/// <summary>What a screen of the video shows.</summary>
public enum ScreenKind
{
    /// <summary>A chapter's title (its part's title above it, when it opens the part).</summary>
    Title,
    /// <summary>A paragraph, or a page of a long one.</summary>
    Paragraph,
    /// <summary>The opening or closing credits.</summary>
    Credits
}

/// <summary>One screen of the video and when it is shown (seconds from the start of its track).</summary>
public sealed record VideoScreen(double Start, double End, ScreenKind Kind, string Text);

/// <summary>A piece of a track as heard: the paragraph it reads (−1 for the title or credits) and when.</summary>
public sealed record HeardPiece(int Paragraph, double Start, double End);

/// <summary>
/// The video of an audiobook track (Ron, 2026-11-09: <i>"each paragraph on the video screen with the
/// narration"</i>; plan phase 11): which paragraph is on the screen when. A paragraph comes up as its
/// first piece is read and stays until the next one starts, so the pauses never show an empty screen;
/// a paragraph too long for one screen is split into pages at sentence ends, each shown for its share of
/// the reading. Each screen comes up <see cref="LeadSeconds"/> before its first word, so the eye is there
/// as the voice starts (measured on a real track: without it the text came a third of a second late).
/// </summary>
public static class VideoScreens
{
    public const double LeadSeconds = 0.3;

    /// <summary>
    /// The screens of one track. <paramref name="fits"/> says whether a text fits on one screen (the App
    /// measures it in the video's font); <paramref name="titleText"/> is what the title or credits screen shows.
    /// </summary>
    public static List<VideoScreen> ForTrack(IReadOnlyList<HeardPiece> pieces, IReadOnlyList<string> paragraphs, string titleText,
                                             ScreenKind titleKind, double totalSeconds, Func<string, bool> fits)
    {
        var screens = new List<VideoScreen>();
        // Consecutive pieces of the same paragraph make one group (a paragraph with dialogue is several pieces).
        var groups = new List<List<HeardPiece>>();
        foreach (var p in pieces)
        {
            if (groups.Count > 0 && groups[^1][0].Paragraph == p.Paragraph) groups[^1].Add(p);
            else groups.Add(new List<HeardPiece> { p });
        }
        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            double start = g == 0 ? 0 : Math.Max(groups[g - 1][^1].End, group[0].Start - LeadSeconds);
            double end = g + 1 < groups.Count ? Math.Max(group[^1].End, groups[g + 1][0].Start - LeadSeconds) : totalSeconds;
            if (end <= start) continue;
            int para = group[0].Paragraph;
            if (para < 0 || para >= paragraphs.Count)
            {
                screens.Add(new VideoScreen(start, end, titleKind, titleText));
                continue;
            }
            var pages = Pages(paragraphs[para], fits);
            if (pages.Count == 1) { screens.Add(new VideoScreen(start, end, ScreenKind.Paragraph, pages[0])); continue; }
            // Each page for its share of the reading, by its length.
            double speakStart = group[0].Start - LeadSeconds, speakEnd = group[^1].End - LeadSeconds;
            double total = pages.Sum(p => p.Length), done = 0;
            double at = start;
            for (int i = 0; i < pages.Count; i++)
            {
                done += pages[i].Length;
                double next = i == pages.Count - 1 ? end : speakStart + (speakEnd - speakStart) * done / total;
                screens.Add(new VideoScreen(at, Math.Max(at, next), ScreenKind.Paragraph, pages[i]));
                at = Math.Max(at, next);
            }
        }
        return screens;
    }

    private static readonly Regex SentenceEnd = new(@"(?<=[.!?…][""”’)\]]*)\s+", RegexOptions.Compiled);

    /// <summary>A paragraph as screens: whole if it fits, else as many sentences as fit on each, else as many words.</summary>
    public static List<string> Pages(string paragraph, Func<string, bool> fits)
    {
        var text = paragraph.Trim();
        if (text.Length == 0 || fits(text)) return new List<string> { text };
        var pages = new List<string>();
        var current = new StringBuilder();
        void Flush() { if (current.Length > 0) { pages.Add(current.ToString()); current.Clear(); } }
        foreach (var sentence in SentenceEnd.Split(text).Where(s => s.Length > 0))
        {
            var tryText = current.Length == 0 ? sentence : current + " " + sentence;
            if (fits(tryText)) { current.Clear().Append(tryText); continue; }
            Flush();
            if (fits(sentence)) { current.Append(sentence); continue; }
            foreach (var word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))     // a sentence longer than a screen
            {
                var withWord = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && !fits(withWord)) { Flush(); current.Append(word); }
                else current.Clear().Append(withWord);
            }
        }
        Flush();
        return pages;
    }

    /// <summary>
    /// Where each piece is heard, worked out from the track's length when the exact times were not kept
    /// (a track made before videos): the head, tail and pauses are known, and the speech is shared out by length.
    /// </summary>
    public static List<HeardPiece> Estimate(IReadOnlyList<(int Paragraph, int Characters, int PauseBeforeMs)> pieces, double totalSeconds)
    {
        double fixedSeconds = (ChapterSound.HeadMs + ChapterSound.TailMs + pieces.Sum(p => p.PauseBeforeMs)) / 1000.0;
        double speaking = Math.Max(0, totalSeconds - fixedSeconds);
        double chars = Math.Max(1, pieces.Sum(p => p.Characters));
        var list = new List<HeardPiece>();
        double at = ChapterSound.HeadMs / 1000.0;
        foreach (var p in pieces)
        {
            at += p.PauseBeforeMs / 1000.0;
            double len = speaking * p.Characters / chars;
            list.Add(new HeardPiece(p.Paragraph, at, at + len));
            at += len;
        }
        return list;
    }

    /// <summary>
    /// Estimated times put right by the sound: where a new paragraph starts, the nearest point where a pause
    /// in the track ends (within <paramref name="toleranceSeconds"/>) is taken as its start.
    /// </summary>
    public static List<HeardPiece> Snap(IReadOnlyList<HeardPiece> estimate, IReadOnlyList<double> pauseEnds, double toleranceSeconds = 2.5)
    {
        var list = estimate.ToList();
        double floor = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (i > 0 && p.Paragraph != list[i - 1].Paragraph)
            {
                var near = pauseEnds.Where(t => t > floor && Math.Abs(t - p.Start) <= toleranceSeconds).OrderBy(t => Math.Abs(t - p.Start)).Cast<double?>().FirstOrDefault();
                if (near is { } t) { list[i] = p with { Start = t, End = Math.Max(t, p.End) }; }
            }
            floor = list[i].Start;
        }
        return list;
    }

    /// <summary>A YouTube time: 0:00, 12:34 or 1:02:03.</summary>
    public static string YouTubeTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    /// <summary>
    /// The video's YouTube description: what it is, then the chapter list YouTube turns into chapters
    /// (it wants the first at 0:00, at least three, each at least ten seconds long).
    /// </summary>
    public static string YouTubeDescription(string title, string author, string narrator, IReadOnlyList<ChapterMark> marks, TimeSpan length)
    {
        var sb = new StringBuilder();
        sb.Append(title).Append(author.Length > 0 ? $", written by {author}" : "").AppendLine(".");
        sb.AppendLine($"Read aloud by a synthesized voice ({narrator}), with the text on the screen as it is read.");
        sb.AppendLine();
        var shown = marks.Where((m, i) => i == 0 || (m.Start - marks[i - 1].Start).TotalSeconds >= 10).ToList();
        bool chapters = shown.Count >= 3 && shown[0].Start == TimeSpan.Zero && (length - shown[^1].Start).TotalSeconds >= 10;
        if (marks.Count > 0)
        {
            sb.AppendLine("Chapters:");
            foreach (var m in chapters ? shown : marks.ToList()) sb.AppendLine($"{YouTubeTime(m.Start)} {m.Title}");
            sb.AppendLine();
        }
        sb.AppendLine("Made with Author+ — https://softwareplus.ai/author/");
        if (!chapters && marks.Count > 0)
            sb.AppendLine().AppendLine("(YouTube makes chapters from a list like the one above only when there are at least three, the first at 0:00, each at least ten seconds long.)");
        return sb.ToString();
    }
}

/// <summary>Pictures as NV12, the form Windows' H.264 encoder takes: brightness for every pixel, colour for every 2 × 2.</summary>
public static class Nv12
{
    /// <summary>A BGRA picture (32 bits a pixel, top row first) as NV12 in the HD colour standard (BT.709, 16–235).</summary>
    public static byte[] FromBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        if (width % 2 != 0 || height % 2 != 0) throw new ArgumentException("NV12 needs an even width and height.");
        var nv12 = new byte[width * height * 3 / 2];
        int uvBase = width * height;
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                nv12[y * width + x] = (byte)Math.Clamp((int)(16.5 + 0.1826 * r + 0.6142 * g + 0.0620 * b), 0, 255);
            }
        }
        for (int y = 0; y < height; y += 2)
            for (int x = 0; x < width; x += 2)
            {
                int r = 0, g = 0, b = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int i = (y + dy) * stride + (x + dx) * 4;
                        b += bgra[i]; g += bgra[i + 1]; r += bgra[i + 2];
                    }
                double R = r / 4.0, G = g / 4.0, B = b / 4.0;
                int at = uvBase + (y / 2) * width + x;
                nv12[at] = (byte)Math.Clamp((int)(128.5 - 0.1006 * R - 0.3386 * G + 0.4392 * B), 0, 255);
                nv12[at + 1] = (byte)Math.Clamp((int)(128.5 + 0.4392 * R - 0.3989 * G - 0.0403 * B), 0, 255);
            }
        return nv12;
    }
}
