using System.Buffers.Binary;
using System.Text;

namespace AuthorPlus.Core.Services.Audio;

/// <summary>One chapter mark: its title and where it starts.</summary>
public sealed record ChapterMark(string Title, TimeSpan Start);

/// <summary>The audiobook's details as players show them.</summary>
public sealed class AudiobookDetails
{
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Narrator { get; init; } = "";
    public string Year { get; init; } = "";
    public string Comment { get; init; } = "";
    /// <summary>A JPEG or PNG picture for the cover, or null.</summary>
    public byte[]? Cover { get; init; }
}

/// <summary>
/// Turns an AAC file (.m4a, as Windows' encoder writes it) into an audiobook (plan 10.7): chapter marks
/// in both forms players read — a QuickTime chapter track (Apple Books, iOS) and a Nero <c>chpl</c> list
/// (VLC, foobar2000, Audiobookshelf …) — and the book's details and cover (iTunes-style tags, marked as an
/// audiobook). The sound itself is copied, not touched; the index (<c>moov</c>) is rewritten at the end.
/// </summary>
public static class Mp4Book
{
    private sealed class Box
    {
        public string Type = "";
        public long Offset, Size;
        public int HeaderSize;
    }

    public static void Write(string m4aPath, string m4bPath, IReadOnlyList<ChapterMark> chapters, AudiobookDetails details)
    {
        using var input = File.OpenRead(m4aPath);
        var top = ReadBoxes(input, 0, input.Length);
        var moovBox = top.SingleOrDefault(b => b.Type == "moov") ?? throw new InvalidDataException("The audio file has no index (moov).");
        input.Position = moovBox.Offset;
        var moov = new byte[moovBox.Size];
        input.ReadExactly(moov);

        // Where each box that is kept lands in the new file: the same order, without the old index.
        var kept = top.Where(b => b != moovBox).ToList();
        long Shift(long oldOffset) => oldOffset > moovBox.Offset ? oldOffset - moovBox.Size : oldOffset;
        long textAt = kept.Count == 0 ? 0 : Shift(kept[^1].Offset) + kept[^1].Size;

        // The chapter track's samples: the titles, in their own small mdat after the sound.
        var samples = chapters.Select(c => TextSample(c.Title)).ToList();
        var textMdat = Concat(Header(8 + samples.Sum(s => s.Length), "mdat"), samples.SelectMany(s => s).ToArray());
        long firstSample = textAt + 8;

        var newMoov = Rebuild(moov, Shift, chapters, samples, firstSample, details);

        var tmp = m4bPath + ".tmp";
        using (var output = File.Create(tmp))
        {
            foreach (var b in kept)
            {
                input.Position = b.Offset;
                CopyBytes(input, output, b.Size);
            }
            output.Write(textMdat);
            output.Write(newMoov);
        }
        File.Move(tmp, m4bPath, overwrite: true);
    }

    // ── The index ─────────────────────────────────────────────────────────────

    private static byte[] Rebuild(byte[] moov, Func<long, long> shift, IReadOnlyList<ChapterMark> chapters, List<byte[]> samples, long firstSample, AudiobookDetails details)
    {
        var children = ReadBoxes(moov, 8, moov.Length);
        var mvhd = children.Single(b => b.Type == "mvhd");
        var mvhdBytes = Slice(moov, mvhd);
        int version = mvhdBytes[8];
        uint timescale = BinaryPrimitives.ReadUInt32BigEndian(mvhdBytes.AsSpan(version == 1 ? 28 : 20));
        ulong duration = version == 1 ? BinaryPrimitives.ReadUInt64BigEndian(mvhdBytes.AsSpan(32)) : BinaryPrimitives.ReadUInt32BigEndian(mvhdBytes.AsSpan(24));
        int nextIdAt = mvhdBytes.Length - 4;
        uint textId = BinaryPrimitives.ReadUInt32BigEndian(mvhdBytes.AsSpan(nextIdAt));
        BinaryPrimitives.WriteUInt32BigEndian(mvhdBytes.AsSpan(nextIdAt), textId + 1);
        double totalSeconds = duration / (double)timescale;

        var parts = new List<byte[]> { mvhdBytes };
        bool chapRef = false;
        foreach (var b in children)
        {
            if (b.Type is "mvhd" or "udta") continue;
            var bytes = Slice(moov, b);
            if (b.Type == "trak")
            {
                bytes = ShiftOffsets(bytes, shift);
                if (!chapRef && IsSoundTrack(bytes)) { bytes = AddChapterReference(bytes, textId); chapRef = true; }
            }
            parts.Add(bytes);
        }
        parts.Add(ChapterTrack(textId, timescale, totalSeconds, chapters, samples, firstSample));

        var udtaChildren = new List<byte[]>();
        if (children.FirstOrDefault(b => b.Type == "udta") is { } oldUdta)
            foreach (var u in ReadBoxes(moov, oldUdta.Offset + oldUdta.HeaderSize, oldUdta.Offset + oldUdta.Size))
                if (u.Type is not ("chpl" or "meta")) udtaChildren.Add(Slice(moov, u));
        udtaChildren.Add(Chpl(chapters));
        udtaChildren.Add(Meta(details));
        parts.Add(BoxOf("udta", udtaChildren.SelectMany(x => x).ToArray()));
        return BoxOf("moov", parts.SelectMany(x => x).ToArray());
    }

    private static bool IsSoundTrack(byte[] trak)
    {
        var mdia = Find(trak, 8, "mdia");
        if (mdia == null) return false;
        var hdlr = Find(trak, mdia.Offset + mdia.HeaderSize, "hdlr", mdia.Offset + mdia.Size);
        return hdlr != null && Encoding.ASCII.GetString(trak, (int)hdlr.Offset + 16, 4) == "soun";
    }

    /// <summary>A trak with a tref/chap pointing at the chapter track, put right after its tkhd.</summary>
    private static byte[] AddChapterReference(byte[] trak, uint textId)
    {
        var kids = ReadBoxes(trak, 8, trak.Length);
        var chap = BoxOf("tref", BoxOf("chap", U32(textId)));
        var body = new List<byte>();
        foreach (var k in kids)
        {
            if (k.Type == "tref") continue;
            body.AddRange(Slice(trak, k));
            if (k.Type == "tkhd") body.AddRange(chap);
        }
        return BoxOf("trak", body.ToArray());
    }

    /// <summary>The trak with every chunk offset (stco / co64) moved to where its sound now is.</summary>
    private static byte[] ShiftOffsets(byte[] trak, Func<long, long> shift)
    {
        var copy = (byte[])trak.Clone();
        void Walk(long from, long to)
        {
            foreach (var b in ReadBoxes(copy, from, to))
            {
                if (b.Type is "mdia" or "minf" or "stbl") Walk(b.Offset + b.HeaderSize, b.Offset + b.Size);
                else if (b.Type == "stco")
                {
                    int n = (int)BinaryPrimitives.ReadUInt32BigEndian(copy.AsSpan((int)b.Offset + 12));
                    for (int i = 0; i < n; i++)
                    {
                        var at = (int)b.Offset + 16 + i * 4;
                        BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(at), (uint)shift(BinaryPrimitives.ReadUInt32BigEndian(copy.AsSpan(at))));
                    }
                }
                else if (b.Type == "co64")
                {
                    int n = (int)BinaryPrimitives.ReadUInt32BigEndian(copy.AsSpan((int)b.Offset + 12));
                    for (int i = 0; i < n; i++)
                    {
                        var at = (int)b.Offset + 16 + i * 8;
                        BinaryPrimitives.WriteUInt64BigEndian(copy.AsSpan(at), (ulong)shift((long)BinaryPrimitives.ReadUInt64BigEndian(copy.AsSpan(at))));
                    }
                }
            }
        }
        Walk(8, copy.Length);
        return copy;
    }

    // ── The chapter track (QuickTime text) ────────────────────────────────────

    private static byte[] TextSample(string title)
    {
        var text = Encoding.UTF8.GetBytes(title.Length > 0 ? title : " ");
        var encd = new byte[] { 0, 0, 0, 12, (byte)'e', (byte)'n', (byte)'c', (byte)'d', 0, 0, 1, 0 };   // the text is UTF-8
        return Concat(U16((ushort)text.Length), text, encd);
    }

    private static byte[] ChapterTrack(uint id, uint movieTimescale, double totalSeconds, IReadOnlyList<ChapterMark> chapters, List<byte[]> samples, long firstSample)
    {
        const uint ms = 1000;
        uint totalMs = (uint)Math.Round(totalSeconds * ms);
        var durations = new List<uint>();
        for (int i = 0; i < chapters.Count; i++)
        {
            var start = (uint)Math.Round(chapters[i].Start.TotalMilliseconds);
            var end = i + 1 < chapters.Count ? (uint)Math.Round(chapters[i + 1].Start.TotalMilliseconds) : totalMs;
            durations.Add(Math.Max(1, end > start ? end - start : 1));
        }
        uint trackMs = (uint)durations.Sum(d => (long)d);
        uint movieDuration = (uint)Math.Round(trackMs / 1000.0 * movieTimescale);

        var tkhd = FullBox("tkhd", 0, 0, Concat(U32(0), U32(0), U32(id), U32(0), U32(movieDuration), new byte[8], U16(0), U16(0), U16(0), U16(0), Matrix(), U32(0), U32(0)));
        var mdhd = FullBox("mdhd", 0, 0, Concat(U32(0), U32(0), U32(ms), U32(trackMs), U16(0x55C4), U16(0)));
        var hdlr = FullBox("hdlr", 0, 0, Concat(U32(0), Ascii("text"), new byte[12], Encoding.ASCII.GetBytes("Chapters"), new byte[] { 0 }));
        var gmin = FullBox("gmin", 0, 0, Concat(U16(0x40), U16(0x8000), U16(0x8000), U16(0x8000), U16(0), U16(0)));
        var gmhd = BoxOf("gmhd", Concat(gmin, BoxOf("text", Matrix())));
        var dinf = BoxOf("dinf", FullBox("dref", 0, 0, Concat(U32(1), FullBox("url ", 0, 1, Array.Empty<byte>()))));
        var stubHeader = new byte[]
        {
            0, 0, 0, 1,  0, 0,  0, 0, 0, 0,          // display flags, justification, background colour
            0, 0, 0, 0, 0, 0, 0, 0,                  // default text box
            0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0,      // style record
            0, 0, 0, 13, (byte)'f', (byte)'t', (byte)'a', (byte)'b', 0, 1, 0, 1, 0   // font table
        };
        var textEntry = BoxOf("text", Concat(new byte[6], U16(1), stubHeader));
        var stsd = FullBox("stsd", 0, 0, Concat(U32(1), textEntry));
        var stts = FullBox("stts", 0, 0, Concat(U32((uint)durations.Count), durations.SelectMany(d => Concat(U32(1), U32(d))).ToArray()));
        var stsz = FullBox("stsz", 0, 0, Concat(U32(0), U32((uint)samples.Count), samples.SelectMany(s => U32((uint)s.Length)).ToArray()));
        var stsc = FullBox("stsc", 0, 0, Concat(U32(1), U32(1), U32((uint)samples.Count), U32(1)));
        var stco = FullBox("stco", 0, 0, Concat(U32(1), U32((uint)firstSample)));
        var stbl = BoxOf("stbl", Concat(stsd, stts, stsz, stsc, stco));
        var minf = BoxOf("minf", Concat(gmhd, dinf, stbl));
        var mdia = BoxOf("mdia", Concat(mdhd, hdlr, minf));
        return BoxOf("trak", Concat(tkhd, mdia));
    }

    // ── Nero chapters and the book's details ──────────────────────────────────

    private static byte[] Chpl(IReadOnlyList<ChapterMark> chapters)
    {
        var body = new List<byte> { 0, 0, 0, 0, (byte)Math.Min(255, chapters.Count) };
        foreach (var c in chapters.Take(255))
        {
            var title = Encoding.UTF8.GetBytes(c.Title);
            if (title.Length > 255) title = title[..255];
            body.AddRange(U64((ulong)(c.Start.Ticks)));      // 100-nanosecond units
            body.Add((byte)title.Length);
            body.AddRange(title);
        }
        return FullBox("chpl", 1, 0, body.ToArray());
    }

    private static byte[] Meta(AudiobookDetails d)
    {
        var items = new List<byte[]>();
        void Text(string key, string value) { if (value.Length > 0) items.Add(BoxOf(key, DataBox(1, Encoding.UTF8.GetBytes(value)))); }
        Text("©nam", d.Title); Text("©alb", d.Title); Text("©ART", d.Author); Text("aART", d.Author);
        Text("©wrt", d.Narrator); Text("©gen", "Audiobook"); Text("©day", d.Year); Text("©cmt", d.Comment);
        items.Add(BoxOf("stik", DataBox(21, new byte[] { 2 })));        // media kind: audiobook
        if (d.Cover is { Length: > 4 } cover)
            items.Add(BoxOf("covr", DataBox(cover[0] == 0x89 && cover[1] == (byte)'P' ? 14u : 13u, cover)));
        var hdlr = FullBox("hdlr", 0, 0, Concat(U32(0), Ascii("mdir"), Ascii("appl"), new byte[8], new byte[] { 0 }));
        return FullBox("meta", 0, 0, Concat(hdlr, BoxOf("ilst", items.SelectMany(x => x).ToArray())));
    }

    private static byte[] DataBox(uint type, byte[] payload) => BoxOf("data", Concat(U32(type), U32(0), payload));

    // ── Box plumbing ──────────────────────────────────────────────────────────

    private static List<Box> ReadBoxes(Stream s, long from, long to)
    {
        var list = new List<Box>();
        var head = new byte[16];
        long p = from;
        while (p + 8 <= to)
        {
            s.Position = p;
            s.ReadExactly(head, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(head);
            int hs = 8;
            if (size == 1) { s.ReadExactly(head, 8, 8); size = (long)BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(8)); hs = 16; }
            else if (size == 0) size = to - p;
            if (size < hs || p + size > to) break;
            list.Add(new Box { Type = Encoding.Latin1.GetString(head, 4, 4), Offset = p, Size = size, HeaderSize = hs });
            p += size;
        }
        return list;
    }

    private static List<Box> ReadBoxes(byte[] b, long from, long to)
    {
        using var ms = new MemoryStream(b, writable: false);
        return ReadBoxes(ms, from, to);
    }

    private static Box? Find(byte[] b, long from, string type, long? to = null) => ReadBoxes(b, from, to ?? b.Length).FirstOrDefault(x => x.Type == type);

    private static byte[] Slice(byte[] b, Box box) => b.AsSpan((int)box.Offset, (int)box.Size).ToArray();

    private static void CopyBytes(Stream from, Stream to, long count)
    {
        var buffer = new byte[1 << 20];
        while (count > 0)
        {
            int n = from.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (n <= 0) throw new EndOfStreamException();
            to.Write(buffer, 0, n);
            count -= n;
        }
    }

    private static byte[] Header(long size, string type) => Concat(U32((uint)size), Ascii(type));
    private static byte[] BoxOf(string type, byte[] body) => Concat(Header(8 + body.Length, type), body);
    private static byte[] FullBox(string type, byte version, uint flags, byte[] body) =>
        BoxOf(type, Concat(new[] { version, (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags }, body));
    private static byte[] Ascii(string s) => Encoding.Latin1.GetBytes(s);
    private static byte[] U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }
    private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }
    private static byte[] U64(ulong v) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, v); return b; }
    private static byte[] Matrix() => Concat(U32(0x00010000), U32(0), U32(0), U32(0), U32(0x00010000), U32(0), U32(0), U32(0), U32(0x40000000));
    private static byte[] Concat(params byte[][] parts) { var r = new byte[parts.Sum(p => p.Length)]; int at = 0; foreach (var p in parts) { p.CopyTo(r, at); at += p.Length; } return r; }

    // ── Reading it back (tests, and the export's own check) ───────────────────

    /// <summary>The chapter titles and starts in a file's Nero list, and whether a chapter track is referenced.</summary>
    public static (IReadOnlyList<ChapterMark> Chapters, bool HasChapterTrack, string? Title) Inspect(string path)
    {
        using var s = File.OpenRead(path);
        var moovBox = ReadBoxes(s, 0, s.Length).Single(b => b.Type == "moov");
        s.Position = moovBox.Offset;
        var moov = new byte[moovBox.Size];
        s.ReadExactly(moov);
        var list = new List<ChapterMark>();
        string? title = null;
        bool track = false;
        foreach (var b in ReadBoxes(moov, 8, moov.Length))
        {
            if (b.Type == "trak" && Find(moov, b.Offset + 8, "tref", b.Offset + b.Size) != null) track = true;
            if (b.Type != "udta") continue;
            foreach (var u in ReadBoxes(moov, b.Offset + 8, b.Offset + b.Size))
            {
                if (u.Type == "chpl")
                {
                    int p = (int)u.Offset + 12 + 4;
                    int n = moov[p++];
                    for (int i = 0; i < n; i++)
                    {
                        long ticks = (long)BinaryPrimitives.ReadUInt64BigEndian(moov.AsSpan(p)); p += 8;
                        int len = moov[p++];
                        list.Add(new ChapterMark(Encoding.UTF8.GetString(moov, p, len), TimeSpan.FromTicks(ticks))); p += len;
                    }
                }
                if (u.Type == "meta" && Find(moov, u.Offset + 12, "ilst", u.Offset + u.Size) is { } ilst &&
                    ReadBoxes(moov, ilst.Offset + 8, ilst.Offset + ilst.Size).FirstOrDefault(x => x.Type == "©nam") is { } nam)
                {
                    var data = (int)nam.Offset + 8;
                    title = Encoding.UTF8.GetString(moov, data + 16, (int)nam.Size - 8 - 16);
                }
            }
        }
        return (list, track, title);
    }
}
