namespace AuthorPlus.Core.Services.Versioning;

/// <summary>
/// The name a version is saved under. Taken from the book's author, so the history of a book
/// written by two people says who wrote what; the e-mail is only what git insists on having.
/// </summary>
public sealed record VersionAuthor(string Name, string Email)
{
    public static readonly VersionAuthor Unknown = new("AuthorPlus", "author@authorplus.local");

    /// <summary>Falls back to the machine's user name, then to <see cref="Unknown"/>.</summary>
    public static VersionAuthor For(string? name, string? email = null)
    {
        var who = string.IsNullOrWhiteSpace(name) ? Environment.UserName : name.Trim();
        if (string.IsNullOrWhiteSpace(who)) return Unknown;
        var mail = string.IsNullOrWhiteSpace(email) ? Slug(who) + "@authorplus.local" : email.Trim();
        return new VersionAuthor(who, mail);
    }

    private static string Slug(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '.').ToArray()).Trim('.');
        return cleaned.Length == 0 ? "author" : cleaned;
    }
}

/// <summary>What happened to one part of the book between two versions.</summary>
public enum BookChangeKind { Added, Edited, Removed, Renamed }

/// <summary>
/// One changed file, said in the book's own words: "Chapter 12 — The Interrogation (the text)"
/// rather than <c>chapters/6f3a….xaml</c>. <see cref="Path"/> is kept because it is what git
/// and the compare view work in.
/// </summary>
public sealed record BookChange(BookChangeKind Kind, string Path, string Description)
{
    public string KindWord => Kind switch
    {
        BookChangeKind.Added   => "Added",
        BookChangeKind.Removed => "Removed",
        BookChangeKind.Renamed => "Renamed",
        _                      => "Changed"
    };

    /// <summary>True for a chapter's prose, the only file a side-by-side compare can read.</summary>
    public bool IsChapterText => Path.StartsWith("chapters/", StringComparison.OrdinalIgnoreCase) &&
                                 Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One saved version of the book: a commit, named for an author rather than a developer.</summary>
public sealed record BookVersion(string Sha, DateTimeOffset When, string AuthorName, string Message)
{
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;

    /// <summary>"15 Sep 2026, 09:14" in the reader's own locale.</summary>
    public string WhenText => When.LocalDateTime.ToString("d MMM yyyy, HH:mm");
}

/// <summary>
/// Something went wrong that the author can do something about — said in words they can act on.
/// Everything libgit2 or GitHub reports is turned into one of these before it leaves this layer.
/// </summary>
public sealed class BookVersioningException : Exception
{
    public BookVersioningException(string message) : base(message) { }
    public BookVersioningException(string message, Exception inner) : base(message, inner) { }
}
