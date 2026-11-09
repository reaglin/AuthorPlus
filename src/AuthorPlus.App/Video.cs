using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuthorPlus.Core.Services.Video;
using NAudio.MediaFoundation;
using NAudio.Wave;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;

namespace AuthorPlus.App;

/// <summary>
/// An MP4 for YouTube written with Windows' own encoders (Media Foundation's sink writer, through NAudio's
/// interop, the route the M4B already takes): H.264 1920 × 1080 pictures and AAC 192 kbps sound. The pictures
/// change only when the screen does, so the encoder is asked for quality, not a bit rate: a still screen costs
/// almost nothing, and a book's video stays a reasonable size.
/// </summary>
internal sealed class Mp4VideoWriter : IDisposable
{
    public const int Width = 1920, Height = 1080, Fps = 5;
    public const int AudioRate = 44100, AudioChannels = 2;

    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MF_MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    private static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MF_MT_YUV_MATRIX = new("3e23d450-2c75-4d25-a00e-b91670d12327");
    private static readonly Guid MF_MT_VIDEO_NOMINAL_RANGE = new("c21b8ee5-b956-4071-8daf-325edf5cab11");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_NV12 = new("3231564e-0000-0010-8000-00aa00389b71");
    private static readonly Guid CODECAPI_AVEncCommonRateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    private static readonly Guid CODECAPI_AVEncCommonQuality = new("fcbf57a3-7ea5-4b0c-9644-69b40c39c391");
    private static readonly Guid CODECAPI_AVEncMPVGOPSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");

    private IMFSinkWriter? _writer;
    private readonly int _video, _audio;
    private bool _finished;

    public Mp4VideoWriter(string path)
    {
        MediaFoundationApi.Startup();
        var attrs = MediaFoundationApi.CreateAttributes(1);
        attrs.SetUINT32(MediaFoundationAttributes.MF_SINK_WRITER_DISABLE_THROTTLING, 1);
        MediaFoundationInterop.MFCreateSinkWriterFromURL(path, null, attrs, out _writer);
        Marshal.ReleaseComObject(attrs);

        // Pictures: H.264 (High profile) out, NV12 in.
        var vOut = MediaFoundationApi.CreateMediaType();
        vOut.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Video);
        vOut.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, MFVideoFormat_H264);
        vOut.SetUINT32(MediaFoundationAttributes.MF_MT_AVG_BITRATE, 1_500_000);
        vOut.SetUINT32(MF_MT_INTERLACE_MODE, 2);                       // progressive
        vOut.SetUINT32(MF_MT_MPEG2_PROFILE, 100);                      // High
        VideoCommon(vOut);
        _writer.AddStream(vOut, out _video);
        var vIn = MediaFoundationApi.CreateMediaType();
        vIn.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Video);
        vIn.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, MFVideoFormat_NV12);
        vIn.SetUINT32(MF_MT_INTERLACE_MODE, 2);
        vIn.SetUINT32(MF_MT_DEFAULT_STRIDE, Width);
        VideoCommon(vIn);
        var enc = MediaFoundationApi.CreateAttributes(3);
        enc.SetUINT32(CODECAPI_AVEncCommonRateControlMode, 3);         // quality-based
        enc.SetUINT32(CODECAPI_AVEncCommonQuality, 70);
        enc.SetUINT32(CODECAPI_AVEncMPVGOPSize, Fps * 4);              // a whole picture every 4 seconds, for seeking
        _writer.SetInputMediaType(_video, vIn, enc);

        // Sound: AAC 192 kbps out, 16-bit stereo PCM in.
        var aOut = MediaFoundationApi.CreateMediaType();
        aOut.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Audio);
        aOut.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, AudioSubtypes.MFAudioFormat_AAC);
        aOut.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        aOut.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioRate);
        aOut.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_NUM_CHANNELS, AudioChannels);
        aOut.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 24000);
        _writer.AddStream(aOut, out _audio);
        var aIn = MediaFoundationApi.CreateMediaType();
        aIn.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Audio);
        aIn.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, AudioSubtypes.MFAudioFormat_PCM);
        aIn.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        aIn.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioRate);
        aIn.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_NUM_CHANNELS, AudioChannels);
        aIn.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_BLOCK_ALIGNMENT, 2 * AudioChannels);
        aIn.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, AudioRate * 2 * AudioChannels);
        _writer.SetInputMediaType(_audio, aIn, null);
        _writer.BeginWriting();
        foreach (var o in new object[] { vOut, vIn, enc, aOut, aIn }) Marshal.ReleaseComObject(o);
    }

    private static void VideoCommon(IMFMediaType t)
    {
        t.SetUINT64(MF_MT_FRAME_SIZE, ((long)Width << 32) | Height);
        t.SetUINT64(MF_MT_FRAME_RATE, ((long)Fps << 32) | 1);
        t.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, (1L << 32) | 1);
        t.SetUINT32(MF_MT_YUV_MATRIX, 1);                               // BT.709
        t.SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, 2);                      // 16–235
    }

    /// <summary>One picture (NV12) at a time, in 100-nanosecond units.</summary>
    public void WriteFrame(byte[] nv12, long time, long duration) => Write(_video, nv12, nv12.Length, time, duration);

    /// <summary>Sound (16-bit stereo PCM at 44.1 kHz), in 100-nanosecond units.</summary>
    public void WriteAudio(byte[] pcm, int count, long time, long duration) => Write(_audio, pcm, count, time, duration);

    private void Write(int stream, byte[] data, int count, long time, long duration)
    {
        var buffer = MediaFoundationApi.CreateMemoryBuffer(count);
        var sample = MediaFoundationApi.CreateSample();
        try
        {
            buffer.Lock(out var ptr, out _, out _);
            Marshal.Copy(data, 0, ptr, count);
            buffer.Unlock();
            buffer.SetCurrentLength(count);
            sample.AddBuffer(buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(duration);
            _writer!.WriteSample(stream, sample);
        }
        finally { Marshal.ReleaseComObject(sample); Marshal.ReleaseComObject(buffer); }
    }

    public void Finish() { _writer!.DoFinalize(); _finished = true; }

    public void Dispose()
    {
        if (_writer != null) { Marshal.ReleaseComObject(_writer); _writer = null; }
        _ = _finished;
    }
}

/// <summary>The video's look: a light page with dark ink, or the reverse.</summary>
internal sealed record VideoLook(Color Background, Color Ink, Color Muted)
{
    public static readonly VideoLook Light = new(Color.FromRgb(0xF7, 0xF3, 0xEA), Color.FromRgb(0x26, 0x24, 0x20), Color.FromRgb(0x8A, 0x84, 0x78));
    public static readonly VideoLook Dark = new(Color.FromRgb(0x14, 0x14, 0x16), Color.FromRgb(0xEC, 0xE6, 0xD8), Color.FromRgb(0x8C, 0x88, 0x80));
    public static VideoLook Of(string name) => name == "Dark" ? Dark : Light;
}

/// <summary>
/// The video's screens drawn (WPF, on the thread that makes the video): the paragraph in a book face in the
/// middle, the book and chapter small at the top; a chapter's title large; the credits with the cover.
/// </summary>
internal sealed class VideoPainter
{
    private const double TextSize = 50, Margin = 180, Top = 150, Bottom = 110;
    private readonly VideoLook _look;
    private readonly Typeface _face = new(new FontFamily("Georgia, Cambria, Times New Roman"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Typeface _ui = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly BitmapSource? _cover;
    public string Header { get; set; } = "";

    public VideoPainter(VideoLook look, byte[]? cover)
    {
        _look = look;
        if (cover != null)
            try
            {
                var img = new BitmapImage();
                img.BeginInit(); img.StreamSource = new MemoryStream(cover); img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit(); img.Freeze();
                _cover = img;
            }
            catch (Exception ex) when (ex is NotSupportedException or IOException or FileFormatException) { _cover = null; }
    }

    private FormattedText Text(string s, double size, Color c, Typeface? face = null, double width = Mp4VideoWriter.Width - 2 * Margin, TextAlignment align = TextAlignment.Left)
    {
        var t = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face ?? _face, size, new SolidColorBrush(c), 1.0)
        { MaxTextWidth = width, TextAlignment = align, LineHeight = size * 1.45, Trimming = TextTrimming.None };
        return t;
    }

    /// <summary>Whether a paragraph fits on one screen in the video's type.</summary>
    public bool Fits(string s) => Text(s, TextSize, _look.Ink).Height <= Mp4VideoWriter.Height - Top - Bottom;

    public RenderTargetBitmap Draw(VideoScreen screen)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(_look.Background), null, new Rect(0, 0, Mp4VideoWriter.Width, Mp4VideoWriter.Height));
            switch (screen.Kind)
            {
                case ScreenKind.Paragraph:
                {
                    if (Header.Length > 0)
                    {
                        var h = Text(Header, 26, _look.Muted, _ui, align: TextAlignment.Center);
                        h.MaxLineCount = 1; h.Trimming = TextTrimming.CharacterEllipsis;
                        dc.DrawText(h, new Point(Margin, 60));
                    }
                    var t = Text(screen.Text, TextSize, _look.Ink);
                    double space = Mp4VideoWriter.Height - Top - Bottom;
                    dc.DrawText(t, new Point(Margin, Top + Math.Max(0, (space - t.Height) / 2)));
                    break;
                }
                case ScreenKind.Title:
                {
                    // Lines: the small ones first (the part, "Chapter 3"), the chapter's title last and large.
                    var lines = screen.Text.Split('\n');
                    var small = lines.Take(lines.Length - 1).Select(l => Text(l, 42, _look.Muted, align: TextAlignment.Center)).ToList();
                    var big = Text(lines[^1], 86, _look.Ink, align: TextAlignment.Center);
                    double h = small.Sum(s => s.Height + 18) + big.Height;
                    double y = (Mp4VideoWriter.Height - h) / 2;
                    foreach (var s in small) { dc.DrawText(s, new Point(Margin, y)); y += s.Height + 18; }
                    dc.DrawText(big, new Point(Margin, y));
                    break;
                }
                default:
                {
                    // Credits: the cover on the left (when there is one), the words beside it.
                    double x = Margin, width = Mp4VideoWriter.Width - 2 * Margin;
                    if (_cover != null)
                    {
                        double ch = 760, cw = ch * _cover.PixelWidth / Math.Max(1, _cover.PixelHeight);
                        if (cw > 760) { cw = 760; ch = cw * _cover.PixelHeight / Math.Max(1, _cover.PixelWidth); }
                        dc.DrawImage(_cover, new Rect(Margin - 40, (Mp4VideoWriter.Height - ch) / 2, cw, ch));
                        x = Margin - 40 + cw + 90; width = Mp4VideoWriter.Width - x - Margin + 40;
                    }
                    var lines = screen.Text.Split('\n');
                    var title = Text(lines[0], 72, _look.Ink, width: width, align: _cover != null ? TextAlignment.Left : TextAlignment.Center);
                    var rest = Text(string.Join("\n", lines.Skip(1)), 40, _look.Muted, width: width, align: _cover != null ? TextAlignment.Left : TextAlignment.Center);
                    double y = (Mp4VideoWriter.Height - title.Height - 40 - rest.Height) / 2;
                    dc.DrawText(title, new Point(x, y));
                    dc.DrawText(rest, new Point(x, y + title.Height + 40));
                    break;
                }
            }
        }
        var bmp = new RenderTargetBitmap(Mp4VideoWriter.Width, Mp4VideoWriter.Height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v);
        bmp.Freeze();
        return bmp;
    }

    public static byte[] ToNv12(BitmapSource bmp)
    {
        int stride = bmp.PixelWidth * 4;
        var px = new byte[stride * bmp.PixelHeight];
        bmp.CopyPixels(px, stride, 0);
        return Nv12.FromBgra(px, bmp.PixelWidth, bmp.PixelHeight, stride);
    }

    /// <summary>A screen saved as a JPEG of <paramref name="width"/> pixels (the YouTube thumbnail, 1280 × 720).</summary>
    public static void SaveJpeg(BitmapSource bmp, string path, int width)
    {
        double scale = width / (double)bmp.PixelWidth;
        var enc = new JpegBitmapEncoder { QualityLevel = 90 };
        enc.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bmp, new ScaleTransform(scale, scale))));
        using var f = File.Create(path);
        enc.Save(f);
    }
}

/// <summary>One track of a video: its sound (the audiobook track's MP3) and its screens, from the start of the track.</summary>
internal sealed record VideoPart(string Mp3, string Header, List<VideoScreen> Screens, double Seconds);

/// <summary>
/// Makes the video: the parts one after another, each screen drawn once and shown for as long as it is read,
/// the sound of each track under it. Runs on a thread of its own (WPF drawing needs one), reporting progress
/// from 0 to 1; Stop leaves no half-written file.
/// </summary>
internal static class VideoMaker
{
    /// <summary>Makes the video; returns where it was saved ("… (new).mp4" beside the name asked for, if that file was in use at the end).</summary>
    public static Task<string> Make(string output, IReadOnlyList<VideoPart> parts, VideoLook look, byte[]? cover, string? thumbnail,
                                    Action<double> progress, CancellationToken ct)
    {
        var done = new TaskCompletionSource<string>();
        var thread = new Thread(() =>
        {
            var tmp = output + ".making.mp4";
            var tmpThumb = thumbnail == null ? null : thumbnail + ".making.jpg";
            try
            {
                Run(tmp, parts, look, cover, tmpThumb, progress, ct);
                var saved = output;
                try { File.Move(tmp, output, overwrite: true); }
                catch (IOException)
                {
                    // The old video is open somewhere: the new one is kept under another name, never thrown away.
                    saved = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + " (new).mp4");
                    File.Move(tmp, saved, overwrite: true);
                }
                if (tmpThumb != null && File.Exists(tmpThumb))
                    try { File.Move(tmpThumb, thumbnail!, overwrite: true); } catch (IOException) { }
                done.SetResult(saved);
            }
            catch (Exception ex)
            {
                foreach (var f in new[] { tmp, tmpThumb })
                    if (f != null) try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (ex is OperationCanceledException) done.SetCanceled(ct); else done.SetException(ex);
            }
        }) { IsBackground = true, Name = "Video" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static void Run(string path, IReadOnlyList<VideoPart> parts, VideoLook look, byte[]? cover, string? thumbnail, Action<double> progress, CancellationToken ct)
    {
        var painter = new VideoPainter(look, cover);
        using var writer = new Mp4VideoWriter(path);
        const long Tick = 10_000_000;                         // 100-ns units in a second
        long frameLen = Tick / Mp4VideoWriter.Fps;
        double total = parts.Sum(p => p.Seconds), offset = 0;
        long frame = 0, audioAt = 0;
        var pcm = new byte[Mp4VideoWriter.AudioRate * 4];     // a second of 16-bit stereo
        bool thumbDone = thumbnail == null;
        foreach (var part in parts)
        {
            painter.Header = part.Header;
            using var reader = Sound(part.Mp3);
            long partEnd = (long)((offset + part.Seconds) * Tick);
            int screen = -1;
            byte[]? picture = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                long t = frame * frameLen;
                if (t >= partEnd) break;
                // The sound a little ahead of the pictures, so the two arrive together.
                while (audioAt < t + Tick)
                {
                    int n = Fill(reader, pcm);
                    long dur = n / 4 * Tick / Mp4VideoWriter.AudioRate;
                    if (n == 0) break;
                    writer.WriteAudio(pcm, n, audioAt, dur);
                    audioAt += dur;
                }
                double local = t / (double)Tick - offset;
                int s = part.Screens.FindIndex(x => local >= x.Start && local < x.End);
                if (s < 0) s = Math.Max(0, screen);
                if (s != screen || picture == null)
                {
                    screen = s;
                    var bmp = part.Screens.Count > 0 ? painter.Draw(part.Screens[s]) : painter.Draw(new VideoScreen(0, 0, ScreenKind.Title, part.Header));
                    picture = VideoPainter.ToNv12(bmp);
                    if (!thumbDone) { VideoPainter.SaveJpeg(bmp, thumbnail!, 1280); thumbDone = true; }
                }
                writer.WriteFrame(picture, t, frameLen);
                frame++;
                if (frame % Mp4VideoWriter.Fps == 0) progress(Math.Min(1, t / (double)Tick / Math.Max(1, total)));
            }
            // The rest of this track's sound (MP3 decoding can run a few milliseconds past its stated length).
            while (audioAt < partEnd)
            {
                int n = Fill(reader, pcm);
                if (n == 0) break;
                long dur = n / 4 * Tick / Mp4VideoWriter.AudioRate;
                writer.WriteAudio(pcm, n, audioAt, dur);
                audioAt += dur;
            }
            audioAt = Math.Max(audioAt, partEnd);
            offset += part.Seconds;
        }
        writer.Finish();
        progress(1);
    }

    /// <summary>A track's sound as 16-bit stereo at 44.1 kHz, whatever the MP3 is.</summary>
    private static WaveStream Sound(string mp3)
    {
        var reader = new MediaFoundationReader(mp3);
        var want = new WaveFormat(Mp4VideoWriter.AudioRate, 16, Mp4VideoWriter.AudioChannels);
        if (reader.WaveFormat.Equals(want)) return reader;
        return new MediaFoundationResampler(reader, want) { ResamplerQuality = 60 }.ToWaveStream(reader);
    }

    private static int Fill(WaveStream s, byte[] buffer)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int n = s.Read(buffer, got, buffer.Length - got);
            if (n == 0) break;
            got += n;
        }
        return got - got % 4;
    }
}
