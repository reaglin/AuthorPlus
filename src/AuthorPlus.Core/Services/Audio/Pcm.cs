using System.Buffers.Binary;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services.Audio;

/// <summary>Sound as numbers: one channel of 16-bit samples at a sample rate.</summary>
public sealed class PcmAudio
{
    public int SampleRate { get; }
    public short[] Samples { get; }
    public PcmAudio(int sampleRate, short[] samples) { SampleRate = sampleRate; Samples = samples; }
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);
}

/// <summary>WAV files (RIFF, 16-bit PCM): read as one channel, written back.</summary>
public static class Wav
{
    /// <summary>The sound in a WAV file; two channels are mixed to one. Throws <see cref="InvalidDataException"/> for anything else.</summary>
    public static PcmAudio Read(byte[] wav)
    {
        if (wav.Length < 12 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F' || wav[8] != 'W' || wav[9] != 'A')
            throw new InvalidDataException("Not a WAV file.");
        int channels = 0, rate = 0, bits = 0, p = 12;
        while (p + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, p, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(p + 4));
            int body = p + 8;
            if (id == "fmt ")
            {
                channels = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(body + 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(body + 14));
            }
            else if (id == "data")
            {
                if (bits != 16 || channels is < 1 or > 2) throw new InvalidDataException("Only 16-bit WAV with one or two channels is read.");
                int len = Math.Min(size < 0 ? int.MaxValue : size, wav.Length - body);
                int frames = len / (2 * channels);
                var s = new short[frames];
                for (int i = 0; i < frames; i++)
                {
                    int at = body + i * 2 * channels;
                    s[i] = channels == 1 ? BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(at))
                         : (short)((BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(at)) + BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(at + 2))) / 2);
                }
                return new PcmAudio(rate, s);
            }
            p = body + size + (size & 1);
        }
        throw new InvalidDataException("The WAV file has no sound in it.");
    }

    public static byte[] Write(PcmAudio a)
    {
        var bytes = new byte[44 + a.Samples.Length * 2];
        void Tag(int at, string s) { for (int i = 0; i < 4; i++) bytes[at + i] = (byte)s[i]; }
        Tag(0, "RIFF"); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8); Tag(8, "WAVE");
        Tag(12, "fmt "); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), a.SampleRate); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), a.SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        Tag(36, "data"); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), a.Samples.Length * 2);
        Buffer.BlockCopy(a.Samples, 0, bytes, 44, a.Samples.Length * 2);
        if (!BitConverter.IsLittleEndian)
            for (int i = 0; i < a.Samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + i * 2), a.Samples[i]);
        return bytes;
    }
}

/// <summary>
/// A chapter's sound put together (plan 10.4): its pieces in order with natural pauses, silence at the
/// head and tail (the stores ask for ½–1 s and 1–5 s), and its loudness evened out.
/// </summary>
public static class ChapterSound
{
    public const int HeadMs = 1000, TailMs = 3000, AfterTitleMs = 1300, InParagraphMs = 250, BetweenParagraphsMs = 650, SceneBreakMs = 1600;

    /// <summary>The pause before a piece, by what comes before it.</summary>
    public static int PauseBefore(ReadPart? previous, ReadPart next, bool sceneBreakBetween) =>
        previous == null ? 0
        : sceneBreakBetween ? SceneBreakMs
        : previous.Paragraph != next.Paragraph ? BetweenParagraphsMs
        : InParagraphMs;

    /// <summary>The pieces joined, each after its pause, with the head and tail silences.</summary>
    public static PcmAudio Join(int sampleRate, IEnumerable<(PcmAudio Sound, int PauseBeforeMs)> pieces) => JoinTimed(sampleRate, pieces).Audio;

    /// <summary>
    /// The pieces joined, and where each one is heard in the result (seconds from the start) — what a
    /// video needs to show each paragraph while it is read (plan phase 11).
    /// </summary>
    public static (PcmAudio Audio, List<(double Start, double End)> Times) JoinTimed(int sampleRate, IEnumerable<(PcmAudio Sound, int PauseBeforeMs)> pieces)
    {
        var all = new List<short>();
        var times = new List<(double, double)>();
        void Silence(int ms) => all.AddRange(new short[(int)((long)sampleRate * ms / 1000)]);
        Silence(HeadMs);
        foreach (var (sound, pause) in pieces)
        {
            if (sound.SampleRate != sampleRate) throw new InvalidDataException($"A piece is at {sound.SampleRate} Hz, not {sampleRate} Hz.");
            Silence(pause);
            double start = all.Count / (double)sampleRate;
            all.AddRange(Trim(sound.Samples));
            times.Add((start, all.Count / (double)sampleRate));
        }
        Silence(TailMs);
        return (new PcmAudio(sampleRate, all.ToArray()), times);
    }

    /// <summary>The piece without the silence the voice leaves at its ends (the pauses are set here instead).</summary>
    public static short[] Trim(short[] s, short threshold = 120)
    {
        int a = 0, b = s.Length - 1;
        while (a < s.Length && Math.Abs((int)s[a]) < threshold) a++;
        while (b > a && Math.Abs((int)s[b]) < threshold) b--;
        if (a >= b) return Array.Empty<short>();
        a = Math.Max(0, a - 200); b = Math.Min(s.Length - 1, b + 400);     // keep the breath in and the fall at the end
        return s[a..(b + 1)];
    }

    /// <summary>Where each pause of at least <paramref name="minMs"/> ends (seconds) — where speech starts again after it.</summary>
    public static List<double> PauseEnds(PcmAudio a, int minMs = 450, short threshold = 400)
    {
        int window = Math.Max(1, a.SampleRate / 100);                 // 10 ms
        var ends = new List<double>();
        int quiet = 0;
        for (int w = 0; w * window < a.Samples.Length; w++)
        {
            int peak = 0;
            for (int i = w * window; i < Math.Min(a.Samples.Length, (w + 1) * window); i++) peak = Math.Max(peak, Math.Abs((int)a.Samples[i]));
            if (peak < threshold) { quiet++; continue; }
            if (quiet * 10 >= minMs) ends.Add(w * window / (double)a.SampleRate);
            quiet = 0;
        }
        return ends;
    }

    /// <summary>Loudness as RMS in dB below full scale (−∞ for silence), over the sound that is not silence.</summary>
    public static double RmsDb(short[] s)
    {
        double sum = 0; long n = 0;
        foreach (var v in s) if (Math.Abs((int)v) > 60) { sum += (double)v * v; n++; }
        if (n == 0) return double.NegativeInfinity;
        return 20 * Math.Log10(Math.Sqrt(sum / n) / 32768.0);
    }

    public static double PeakDb(short[] s)
    {
        int peak = 0;
        foreach (var v in s) peak = Math.Max(peak, Math.Abs((int)v));
        return peak == 0 ? double.NegativeInfinity : 20 * Math.Log10(peak / 32768.0);
    }

    /// <summary>
    /// The chapter at a steady loudness: raised or lowered to <paramref name="targetRmsDb"/> (the stores ask
    /// for −23 to −18 dB), then any peak held under <paramref name="peakDb"/> by a gentle limiter (fast to
    /// catch, slow to let go, so it does not pump).
    /// </summary>
    public static PcmAudio Even(PcmAudio a, double targetRmsDb = -20, double peakDb = -3.8)
    {
        var rms = RmsDb(a.Samples);
        if (double.IsNegativeInfinity(rms)) return a;
        double gain = Math.Pow(10, (targetRmsDb - rms) / 20);
        double ceiling = 32768 * Math.Pow(10, peakDb / 20);
        double release = Math.Exp(-1.0 / (0.08 * a.SampleRate));   // 80 ms
        double env = 1.0;
        int look = Math.Max(1, a.SampleRate / 500);                  // 2 ms look-ahead
        int n = a.Samples.Length;
        // What each sample needs (1 = nothing), then the least over the next few samples (a sliding minimum).
        var need = new double[n];
        for (int i = 0; i < n; i++) { double x = Math.Abs(a.Samples[i] * gain); need[i] = x > ceiling ? ceiling / x : 1.0; }
        var ahead = new double[n];
        var window = new LinkedList<int>();
        for (int i = n - 1; i >= 0; i--)
        {
            while (window.Count > 0 && need[window.Last!.Value] >= need[i]) window.RemoveLast();
            window.AddLast(i);
            while (window.First!.Value >= i + look) window.RemoveFirst();
            ahead[i] = need[window.First.Value];
        }
        var outS = new short[n];
        for (int i = 0; i < n; i++)
        {
            double needNow = ahead[i];
            env = needNow < env ? needNow : release * env + (1 - release) * needNow;
            double y = a.Samples[i] * gain * env;
            outS[i] = (short)Math.Clamp(Math.Round(y), -ceiling, ceiling);
        }
        return new PcmAudio(a.SampleRate, outS);
    }

    /// <summary>
    /// A chapter's sound fingerprint: everything that changes what it sounds like (the words of each piece,
    /// its voice and pace, the phonetics, the title, the pauses' rules). Same fingerprint, no need to make it again.
    /// </summary>
    public static string Fingerprint(string title, IEnumerable<(string Voice, double Rate, string Key)> pieces)
    {
        var sb = new System.Text.StringBuilder($"v2|-20,-3.8|{HeadMs},{TailMs},{AfterTitleMs},{InParagraphMs},{BetweenParagraphsMs},{SceneBreakMs}|{title}");
        foreach (var (v, r, k) in pieces) sb.Append('\n').Append(v).Append('|').Append(r.ToString("0.00")).Append('|').Append(k);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
