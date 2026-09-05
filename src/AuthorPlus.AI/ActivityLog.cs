using System.Text;
using System.Text.RegularExpressions;

namespace AuthorPlus.AI;

/// <summary>
/// Fire-and-forget activity logger for AI operations (adapted from CIATLE.AICore).
///
/// Call <see cref="Initialize"/> once at startup with the log directory. Without it, entries go
/// to %LOCALAPPDATA%\AuthorPlus\logs. Large prompt/response bodies are offloaded to
/// <c>{logDir}/prompts/{context}/{timestamp}_{type}.prompt</c> so the main log stays compact.
///
/// All writes are asynchronous, serialised with a lock, and silently discarded on failure —
/// this logger never throws and never slows the caller.
/// </summary>
public static class ActivityLog
{
    private static readonly object WriteLock = new();
    private static string? _logDir;
    private static string? _logPath;
    private const string LogFileName = "authorplus-ai.log";
    private const int PromptBodyThreshold = 200;

    public static string? LogDirectory => _logDir ?? ResolveLogDir();

    public static void Initialize(string logDir)
    {
        try
        {
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, LogFileName);
            File.AppendAllText(path, string.Empty, Encoding.UTF8);
            _logDir  = logDir;
            _logPath = path;
        }
        catch { /* lazy fallback on first write */ }
    }

    public static void Info(string context, string message) => Write("INFO ", context, message);
    public static void Warn(string context, string message) => Write("WARN ", context, message);

    public static void Error(string context, string message, Exception? ex = null)
    {
        var full = ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", context, full);
    }

    /// <summary>
    /// Logs an AI call. A message of the form "META>>\nBODY" with a long body writes the body
    /// to a .prompt file and records the path instead.
    /// </summary>
    public static void AiCall(string context, string message)
    {
        const string sep = ">>\n";
        var splitIdx = message.IndexOf(sep, StringComparison.Ordinal);
        if (splitIdx >= 0)
        {
            var metadata = message[..splitIdx].Trim();
            var body     = message[(splitIdx + sep.Length)..];
            if (body.Length >= PromptBodyThreshold)
            {
                var ts   = DateTime.Now;
                var file = SavePromptFile(context, ts, ExtractTypeTag(metadata), body);
                Write("AI   ", context, file is not null ? $"{metadata} → {MakeRelative(file)}" : $"{metadata} (body not saved)", ts);
                return;
            }
        }
        Write("AI   ", context, message);
    }

    private static string? SavePromptFile(string context, DateTime ts, string typeTag, string body)
    {
        try
        {
            var dir = ResolveLogDir();
            if (dir is null) return null;

            var promptDir = Path.Combine(dir, "prompts", SanitizeName(context.Split('/')[0]));
            Directory.CreateDirectory(promptDir);

            var filePath = Path.Combine(promptDir, $"{ts:yyyy-MM-dd_HH-mm-ss}_{typeTag}.prompt");
            for (int n = 2; File.Exists(filePath); n++)
                filePath = Path.Combine(promptDir, $"{ts:yyyy-MM-dd_HH-mm-ss}_{typeTag}_{n}.prompt");

            lock (WriteLock)
            {
                File.WriteAllText(filePath,
                    $"Context:   {context}\r\nTimestamp: {ts:yyyy-MM-dd HH:mm:ss.fff}\r\nType:      {typeTag}\r\n" +
                    $"Length:    {body.Length} chars\r\n{new string('─', 60)}\r\n\r\n{body}",
                    Encoding.UTF8);
            }
            return filePath;
        }
        catch { return null; }
    }

    private static void Write(string level, string context, string message, DateTime? tsOverride = null)
    {
        var ts    = tsOverride ?? DateTime.Now;
        var entry = $"[{ts:yyyy-MM-dd HH:mm:ss.fff}]  {level}  {context,-32}  {message}";

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var path = _logPath ?? (ResolveLogDir() is null ? null : _logPath);
                if (path is null) return;
                lock (WriteLock)
                    File.AppendAllText(path, entry + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* never affect the caller */ }
        });
    }

    private static string? ResolveLogDir()
    {
        if (_logDir is not null) return _logDir;
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuthorPlus", "logs");
        try
        {
            Directory.CreateDirectory(fallback);
            var path = Path.Combine(fallback, LogFileName);
            File.AppendAllText(path, string.Empty, Encoding.UTF8);
            _logDir  = fallback;
            _logPath = path;
        }
        catch { /* give up */ }
        return _logDir;
    }

    private static string ExtractTypeTag(string metadata)
    {
        var m = Regex.Match(metadata, @"^([A-Z]+)");
        return m.Success ? m.Groups[1].Value : "BODY";
    }

    private static string MakeRelative(string fullPath)
    {
        if (_logDir is null) return fullPath;
        try
        {
            var rel = Path.GetRelativePath(_logDir, fullPath);
            return rel.Length < fullPath.Length ? rel : fullPath;
        }
        catch { return fullPath; }
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }
}
