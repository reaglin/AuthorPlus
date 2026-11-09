using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

/// <summary>The list behind Generated Audio/Video: what was made from the book, wherever it was saved.</summary>
public class MadeMediaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ap-made-" + Guid.NewGuid().ToString("N"));
    public MadeMediaTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void An_empty_book_has_nothing_made()
    {
        Assert.Empty(MadeMedia.Load(_dir));
        Directory.CreateDirectory(Path.Combine(_dir, "audio"));
        File.WriteAllText(MadeMedia.ListPath(_dir), "{ not json");
        Assert.Empty(MadeMedia.Load(_dir));                                   // a damaged list is an empty one, not an error
    }

    [Fact]
    public void Made_files_are_kept_newest_first_and_a_remake_replaces_its_entry()
    {
        var video = Path.Combine(_dir, "Videos", "Chapter 1.mp4");
        MadeMedia.Record(_dir, new MadeFile { Kind = MadeKind.Video, Title = "Chapter 1", Path = video, Seconds = 61, MadeUtc = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc) });
        MadeMedia.Record(_dir, new MadeFile { Kind = MadeKind.Audiobook, Title = "The book", Path = Path.Combine(_dir, "Audiobook"), Listen = Path.Combine(_dir, "Audiobook", "The book.m4b"), Tracks = 5, MadeUtc = new DateTime(2026, 11, 9, 11, 0, 0, DateTimeKind.Utc) });
        MadeMedia.Record(_dir, new MadeFile { Kind = MadeKind.Video, Title = "Chapter 1 again", Path = video.ToUpperInvariant(), Seconds = 62, MadeUtc = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc) });

        var list = MadeMedia.Load(_dir);
        Assert.Equal(2, list.Count);
        Assert.Equal("Chapter 1 again", list[0].Title);                        // the same file made again: one entry, the new one
        Assert.Equal(MadeKind.Audiobook, list[1].Kind);
        Assert.Equal(5, list[1].Tracks);
        Assert.Contains("\"Video\"", File.ReadAllText(MadeMedia.ListPath(_dir)));   // kinds written as words
    }

    [Fact]
    public void Forgetting_takes_it_off_the_list_and_leaves_the_file()
    {
        var video = Path.Combine(_dir, "v.mp4");
        File.WriteAllText(video, "x");
        var made = new MadeFile { Kind = MadeKind.Video, Title = "v", Path = video, MadeUtc = DateTime.UtcNow };
        MadeMedia.Record(_dir, made);
        Assert.True(MadeMedia.Load(_dir)[0].IsThere);
        MadeMedia.Forget(_dir, made);
        Assert.Empty(MadeMedia.Load(_dir));
        Assert.True(File.Exists(video));
    }

    [Fact]
    public void Videos_and_audiobooks_made_before_the_list_are_found_once()
    {
        var videos = Path.Combine(_dir, "Videos"); Directory.CreateDirectory(videos);
        File.WriteAllText(Path.Combine(videos, "Chapter 1.mp4"), "v");
        File.WriteAllText(Path.Combine(videos, "Chapter 1 — thumbnail.jpg"), "j");
        File.WriteAllText(Path.Combine(videos, "half.making.mp4"), "v");               // one being made is not a video yet
        var book = Path.Combine(_dir, "Audiobook"); Directory.CreateDirectory(book);
        File.WriteAllText(Path.Combine(book, "01 One.mp3"), "a"); File.WriteAllText(Path.Combine(book, "02 Two.mp3"), "a");
        File.WriteAllText(Path.Combine(book, "The book.m4b"), "m");
        var empty = Path.Combine(_dir, "Nothing"); Directory.CreateDirectory(empty);
        var bookFolder = Path.Combine(_dir, "Book");

        MadeMedia.FindOlder(bookFolder, new[] { videos, Path.Combine(_dir, "none") }, new[] { book, empty });
        var list = MadeMedia.Load(bookFolder);
        Assert.Equal(2, list.Count);
        var v = list.Single(f => f.Kind == MadeKind.Video);
        Assert.Equal("Chapter 1", v.Title);
        Assert.EndsWith("thumbnail.jpg", v.Thumbnail);
        Assert.Null(v.Description);
        var a = list.Single(f => f.Kind == MadeKind.Audiobook);
        Assert.Equal(2, a.Tracks);
        Assert.EndsWith("The book.m4b", a.Listen);

        MadeMedia.Forget(bookFolder, v);                                              // taken off the list, it stays off
        MadeMedia.FindOlder(bookFolder, new[] { videos }, new[] { book });
        Assert.Single(MadeMedia.Load(bookFolder));
    }

    [Fact]
    public void A_moved_file_is_noticed()
    {
        MadeMedia.Record(_dir, new MadeFile { Kind = MadeKind.Video, Title = "gone", Path = Path.Combine(_dir, "gone.mp4"), MadeUtc = DateTime.UtcNow });
        MadeMedia.Record(_dir, new MadeFile { Kind = MadeKind.Audiobook, Title = "here", Path = _dir, MadeUtc = DateTime.UtcNow });
        var list = MadeMedia.Load(_dir);
        Assert.False(list.Single(f => f.Kind == MadeKind.Video).IsThere);
        Assert.True(list.Single(f => f.Kind == MadeKind.Audiobook).IsThere);
    }
}
