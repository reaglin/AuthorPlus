using System.Text.Json;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>
/// Daily writing progress for one book: <c>progress.json</c> in the book folder records, per
/// local date, the word count when the book was first opened that day and the latest count.
/// "Words today" = latest − first. Nothing else is tracked; the file is small forever.
/// </summary>
public sealed class Progress
{
    public const string FileName = "progress.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public sealed class Day
    {
        public int StartWords { get; set; }
        public int LatestWords { get; set; }
        public int Added => LatestWords - StartWords;
    }

    /// <summary>Keyed by "yyyy-MM-dd" (local date).</summary>
    public SortedDictionary<string, Day> Days { get; set; } = new(StringComparer.Ordinal);

    public static string PathFor(Book book) => Path.Combine(book.FolderPath, FileName);

    public static Progress Load(Book book)
    {
        try
        {
            var path = PathFor(book);
            if (!File.Exists(path)) return new Progress();
            return JsonSerializer.Deserialize<Progress>(File.ReadAllText(path), Json) ?? new Progress();
        }
        catch { return new Progress(); }
    }

    public void Save(Book book)
    {
        try
        {
            Directory.CreateDirectory(book.FolderPath);
            BookStore.WriteAtomic(PathFor(book), JsonSerializer.Serialize(this, Json));
        }
        catch { /* progress is a convenience, never worth failing a save over */ }
    }

    public static string TodayKey(DateTime? now = null) => (now ?? DateTime.Now).ToString("yyyy-MM-dd");

    /// <summary>Call when the book is opened and after every save: records today's start on first sight, updates the latest.</summary>
    public Day Touch(int totalWords, DateTime? now = null)
    {
        var key = TodayKey(now);
        if (!Days.TryGetValue(key, out var day))
        {
            day = new Day { StartWords = totalWords, LatestWords = totalWords };
            Days[key] = day;
            // Keep the file small: a year of days is plenty.
            while (Days.Count > 400) Days.Remove(Days.Keys.First());
        }
        day.LatestWords = totalWords;
        return day;
    }

    public int WordsToday(DateTime? now = null) => Days.TryGetValue(TodayKey(now), out var d) ? d.Added : 0;
}
