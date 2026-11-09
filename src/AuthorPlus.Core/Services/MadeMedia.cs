using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthorPlus.Core.Services;

public enum MadeKind { Audiobook, Video }

/// <summary>A video or an exported audiobook the program made from the book, wherever the author saved it.</summary>
public sealed class MadeFile
{
    public MadeKind Kind { get; set; }
    public string Title { get; set; } = "";
    /// <summary>The video file, or the audiobook's folder.</summary>
    public string Path { get; set; } = "";
    /// <summary>An audiobook's M4B (null when none was made).</summary>
    public string? Listen { get; set; }
    public string? Thumbnail { get; set; }
    public string? Description { get; set; }
    public double Seconds { get; set; }
    public int Tracks { get; set; }
    public DateTime MadeUtc { get; set; }

    /// <summary>The file (or folder) is still where it was saved.</summary>
    [JsonIgnore] public bool IsThere => Kind == MadeKind.Video ? File.Exists(Path) : Directory.Exists(Path);
}

/// <summary>
/// The list of what was made from a book (Generated Audio/Video, Ron 2026-11-09), kept in the book's
/// <c>audio\made.json</c>: with the spoken audio, so it is never versioned or exported (the paths are this computer's).
/// </summary>
public static class MadeMedia
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string ListPath(string bookFolder) => System.IO.Path.Combine(bookFolder, "audio", "made.json");

    /// <summary>Newest first; a missing or damaged list is an empty one.</summary>
    public static List<MadeFile> Load(string bookFolder)
    {
        try
        {
            var path = ListPath(bookFolder);
            if (!File.Exists(path)) return new();
            return (JsonSerializer.Deserialize<List<MadeFile>>(File.ReadAllText(path), Json) ?? new())
                .Where(f => f.Path.Length > 0).OrderByDescending(f => f.MadeUtc).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    /// <summary>Adds what was just made; one made again at the same place replaces the old entry.</summary>
    public static void Record(string bookFolder, MadeFile made)
    {
        var list = Load(bookFolder).Where(f => !Same(f, made)).Prepend(made).ToList();
        Save(bookFolder, list);
    }

    /// <summary>Takes an entry off the list (the file itself is left alone).</summary>
    public static void Forget(string bookFolder, MadeFile made) =>
        Save(bookFolder, Load(bookFolder).Where(f => !Same(f, made)).ToList());

    /// <summary>
    /// The first time a book's list is opened: the videos and audiobooks already in their usual folders (made before the list
    /// was kept) go on it, beside anything made since. Only once (a mark in audio\made.found) — after that the list is what
    /// was made and what the author kept on it.
    /// </summary>
    public static void FindOlder(string bookFolder, IEnumerable<string> videoFolders, IEnumerable<string> audiobookFolders)
    {
        var mark = System.IO.Path.Combine(bookFolder, "audio", "made.found");
        if (File.Exists(mark)) return;
        var found = new List<MadeFile>();
        foreach (var dir in videoFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var mp4 in Directory.GetFiles(dir, "*.mp4").Where(f => !f.EndsWith(".making.mp4", StringComparison.OrdinalIgnoreCase)))
            {
                var stem = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(mp4));
                string? Beside(string suffix) => File.Exists(stem + suffix) ? stem + suffix : null;
                found.Add(new MadeFile
                {
                    Kind = MadeKind.Video, Title = System.IO.Path.GetFileNameWithoutExtension(mp4), Path = mp4, MadeUtc = File.GetLastWriteTimeUtc(mp4),
                    Thumbnail = Beside(" — thumbnail.jpg"), Description = Beside(" — YouTube description.txt")
                });
            }
        foreach (var dir in audiobookFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var mp3s = Directory.GetFiles(dir, "*.mp3");
            var m4b = Directory.GetFiles(dir, "*.m4b").FirstOrDefault();
            if (mp3s.Length == 0 && m4b == null) continue;
            found.Add(new MadeFile
            {
                Kind = MadeKind.Audiobook, Title = new DirectoryInfo(dir).Name, Path = dir, Listen = m4b, Tracks = mp3s.Length,
                MadeUtc = mp3s.Append(m4b).OfType<string>().Max(File.GetLastWriteTimeUtc)
            });
        }
        var list = Load(bookFolder);
        list.AddRange(found.Where(f => !list.Any(l => Same(l, f))));
        Save(bookFolder, list.OrderByDescending(f => f.MadeUtc).ToList());
        File.WriteAllText(mark, DateTime.UtcNow.ToString("O"));
    }

    private static bool Same(MadeFile a, MadeFile b) =>
        a.Kind == b.Kind && string.Equals(System.IO.Path.GetFullPath(a.Path).TrimEnd('\\', '/'), System.IO.Path.GetFullPath(b.Path).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static void Save(string bookFolder, List<MadeFile> list)
    {
        var path = ListPath(bookFolder);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(list, Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
