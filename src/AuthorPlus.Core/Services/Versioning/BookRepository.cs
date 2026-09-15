using AuthorPlus.Core.Models;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;

namespace AuthorPlus.Core.Services.Versioning;

/// <summary>
/// The book folder as a git repository — the whole of git AuthorPlus uses, said in the book's
/// own words. A version is a commit; the history is the log; restoring is a new version whose
/// content is the old one (nothing is ever thrown away); backing up is a push to GitHub.
///
/// Why git at all: the folder is already one file per item (see <see cref="BookStore"/>), which
/// is exactly what git is good at — a chapter's revisions are a text diff, and two computers can
/// share a book through a GitHub repository the author never has to look at.
///
/// Why libgit2 rather than git.exe: a reader who buys AuthorPlus from the Store has no Git
/// installed, and asking a novelist to install one would end the feature there.
///
/// Every operation here is synchronous and can take seconds (a push over a slow line, a first
/// version of a 160,000-word book) — call it off the UI thread.
/// </summary>
public sealed class BookRepository : IDisposable
{
    public const string RemoteName = "origin";
    public const string BranchName = "main";

    /// <summary>Written on the first version so git leaves our files exactly as we wrote them.</summary>
    private const string GitAttributes =
        "# AuthorPlus writes every file itself and reads it back byte for byte.\n" +
        "# No end-of-line conversion, on any computer.\n" +
        "* -text\n";

    /// <summary>
    /// What is the author's work and what is this machine's bookkeeping. <c>progress.json</c>
    /// counts the words written today on this computer — versioning it would make every
    /// computer disagree about a number that is not part of the book.
    /// </summary>
    private const string GitIgnore =
        "# Half-written files (BookStore writes to .tmp and then moves).\n" +
        "*.tmp\n" +
        "\n# Today's word count on this computer, not part of the book.\n" +
        "progress.json\n" +
        "\n# Exports are made from the book; they are not the book.\n" +
        "*.docx\n*.pdf\n*.epub\n";

    private const CommitSortStrategies Newest = CommitSortStrategies.Time | CommitSortStrategies.Topological;

    private readonly Repository _repo;

    public string Folder { get; }

    private BookRepository(Repository repo, string folder)
    {
        _repo = repo;
        Folder = folder;
    }

    // ── Opening ───────────────────────────────────────────────────────────────

    /// <summary>True when this book folder is itself a repository (a repository above it does not count).</summary>
    public static bool IsVersioned(string bookFolder) =>
        Directory.Exists(Path.Combine(bookFolder, ".git")) && Repository.IsValid(bookFolder);

    /// <summary>The repository for an already-versioned book, or null.</summary>
    public static BookRepository? TryOpen(string bookFolder)
    {
        if (!IsVersioned(bookFolder)) return null;
        try { return new BookRepository(new Repository(bookFolder), bookFolder); }
        catch (LibGit2SharpException) { return null; }
    }

    /// <summary>
    /// Starts versioning a book folder: a repository on a branch called <c>main</c> (what GitHub
    /// expects), the two settings files, and a first version holding the book as it is today.
    /// </summary>
    public static BookRepository Initialize(string bookFolder, VersionAuthor author)
    {
        if (!Directory.Exists(bookFolder))
            throw new BookVersioningException("That book folder no longer exists.");
        if (IsVersioned(bookFolder))
            return TryOpen(bookFolder) ?? throw new BookVersioningException("The book folder has a damaged .git folder; versioning could not be started.");

        try
        {
            Repository.Init(bookFolder);
            var repo = new Repository(bookFolder);
            // Before the first version, so the branch is "main" and never "master".
            repo.Refs.UpdateTarget("HEAD", "refs/heads/" + BranchName);

            File.WriteAllText(Path.Combine(bookFolder, ".gitattributes"), GitAttributes);
            File.WriteAllText(Path.Combine(bookFolder, ".gitignore"), GitIgnore);

            var versioned = new BookRepository(repo, bookFolder);
            versioned.Commit("The book as it is today", author);
            return versioned;
        }
        catch (LibGit2SharpException ex)
        {
            throw new BookVersioningException("Versioning could not be started for this book: " + ex.Message, ex);
        }
    }

    // ── Versions ──────────────────────────────────────────────────────────────

    /// <summary>Everything changed since the last version, described in the book's words.</summary>
    public IReadOnlyList<BookChange> PendingChanges(Book? book = null)
    {
        var status = _repo.RetrieveStatus(new StatusOptions { IncludeUntracked = true, RecurseUntrackedDirs = true });
        var changes = new List<BookChange>();
        foreach (var entry in status)
        {
            if (entry.State is FileStatus.Ignored or FileStatus.Unaltered) continue;
            var kind = entry.State switch
            {
                var s when s.HasFlag(FileStatus.NewInWorkdir) || s.HasFlag(FileStatus.NewInIndex) => BookChangeKind.Added,
                var s when s.HasFlag(FileStatus.DeletedFromWorkdir) || s.HasFlag(FileStatus.DeletedFromIndex) => BookChangeKind.Removed,
                var s when s.HasFlag(FileStatus.RenamedInWorkdir) || s.HasFlag(FileStatus.RenamedInIndex) => BookChangeKind.Renamed,
                _ => BookChangeKind.Edited
            };
            var path = entry.FilePath.Replace('\\', '/');
            changes.Add(new BookChange(kind, path, ChangeNames.Describe(path, book)));
        }
        return Sorted(changes);
    }

    public bool HasPendingChanges() => PendingChanges().Count > 0;

    /// <summary>
    /// Saves everything in the book folder as one version. Returns null when nothing has changed
    /// since the last one — an author who presses "Save a version" twice has lost nothing.
    /// </summary>
    public BookVersion? SaveVersion(string message, VersionAuthor author)
    {
        if (!HasPendingChanges()) return null;
        return Commit(string.IsNullOrWhiteSpace(message) ? "Saved a version" : message.Trim(), author);
    }

    private BookVersion Commit(string message, VersionAuthor author)
    {
        try
        {
            Commands.Stage(_repo, "*");
            var signature = new Signature(author.Name, author.Email, DateTimeOffset.Now);
            var commit = _repo.Commit(message, signature, signature, new CommitOptions { AllowEmptyCommit = true });
            return Describe(commit);
        }
        catch (LibGit2SharpException ex)
        {
            throw new BookVersioningException("This version could not be saved: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// The saved versions, newest first. Sorted by time <em>and</em> by descent, so a version
    /// never comes before the one it was built on — several versions saved in the same second
    /// (a restore saves two) would otherwise be in any order at all.
    /// </summary>
    public IReadOnlyList<BookVersion> History(int limit = 300)
    {
        if (_repo.Head.Tip is null) return Array.Empty<BookVersion>();
        return _repo.Commits.QueryBy(new CommitFilter { SortBy = Newest })
                   .Take(limit).Select(Describe).ToList();
    }

    /// <summary>The versions that touched one file — a chapter's own history.</summary>
    public IReadOnlyList<BookVersion> HistoryOf(string relativePath, int limit = 100)
    {
        if (_repo.Head.Tip is null) return Array.Empty<BookVersion>();
        try
        {
            return _repo.Commits
                .QueryBy(relativePath.Replace('\\', '/'), new CommitFilter { SortBy = Newest })
                .Take(limit).Select(e => Describe(e.Commit)).ToList();
        }
        catch (LibGit2SharpException)
        {
            return Array.Empty<BookVersion>();   // a path git has never heard of
        }
    }

    /// <summary>What one version changed, against the version before it.</summary>
    public IReadOnlyList<BookChange> ChangesIn(string sha, Book? book = null)
    {
        var commit = Lookup(sha);
        var parent = commit.Parents.FirstOrDefault();
        var diff = _repo.Diff.Compare<TreeChanges>(parent?.Tree, commit.Tree);

        var changes = new List<BookChange>();
        foreach (var change in diff)
        {
            var kind = change.Status switch
            {
                LibGit2Sharp.ChangeKind.Added   => BookChangeKind.Added,
                LibGit2Sharp.ChangeKind.Deleted => BookChangeKind.Removed,
                LibGit2Sharp.ChangeKind.Renamed => BookChangeKind.Renamed,
                _                               => BookChangeKind.Edited
            };
            var path = change.Path.Replace('\\', '/');
            // A file this version removed is not in the book any more, so read its own record at
            // the version before, and the line can still say which chapter it was.
            var where = kind == BookChangeKind.Removed ? parent?.Sha : commit.Sha;
            changes.Add(new BookChange(kind, path,
                ChangeNames.Describe(path, book, p => where is null ? null : ReadTextAt(where, p))));
        }
        return Sorted(changes);
    }

    /// <summary>The version this one was saved on top of, or null for the first one of all.</summary>
    public string? ParentOf(string sha) => Lookup(sha).Parents.FirstOrDefault()?.Sha;

    /// <summary>A file as it was at a version, or null when it did not exist then.</summary>
    public string? ReadTextAt(string sha, string relativePath)
    {
        var commit = Lookup(sha);
        var entry = commit[relativePath.Replace('\\', '/')];
        return entry?.Target is Blob blob ? blob.GetContentText() : null;
    }

    /// <summary>
    /// Puts the book back as it was at <paramref name="sha"/> — as a new version on top of the
    /// history, never by erasing what came after. Anything unsaved is saved as its own version
    /// first, so the work of the last hour is still in the history if the author changes their mind.
    /// </summary>
    public BookVersion Restore(string sha, VersionAuthor author)
    {
        var target = Lookup(sha);
        if (HasPendingChanges()) Commit("Work in progress, saved before restoring an older version", author);

        if (_repo.Refs[_repo.Head.CanonicalName] is not DirectReference head || _repo.Head.Tip is not { } tip)
            throw new BookVersioningException("This book has no saved versions to restore from.");

        try
        {
            // A hard reset puts the folder and the index back to the old version — and takes the
            // branch with it, so the branch goes back on the tip before committing. The new
            // version then carries the old content with the whole history behind it.
            _repo.Reset(ResetMode.Hard, target);
            _repo.Refs.UpdateTarget(head, tip.Id);
            return Commit($"Restored the version from {target.Author.When.LocalDateTime:d MMM yyyy, HH:mm} — “{Shorten(target.MessageShort)}”", author);
        }
        catch (LibGit2SharpException ex)
        {
            throw new BookVersioningException("The book could not be put back to that version: " + ex.Message, ex);
        }
    }

    // ── GitHub ────────────────────────────────────────────────────────────────

    public string? RemoteUrl => _repo.Network.Remotes[RemoteName]?.Url;

    public bool HasRemote => RemoteUrl is not null;

    /// <summary>Points the book at a GitHub repository (replacing any earlier one).</summary>
    public void SetRemote(string url)
    {
        if (_repo.Network.Remotes[RemoteName] is not null) _repo.Network.Remotes.Remove(RemoteName);
        _repo.Network.Remotes.Add(RemoteName, url);
    }

    /// <summary>Sends every version this computer has to GitHub.</summary>
    public SyncOutcome Push(GitHubCredentials credentials, bool force = false)
    {
        if (!HasRemote) throw new BookVersioningException("This book is not connected to GitHub yet.");
        if (_repo.Head.Tip is null) throw new BookVersioningException("Save a version before backing the book up.");

        var branch = _repo.Head.FriendlyName;
        var spec = $"{(force ? "+" : "")}refs/heads/{branch}:refs/heads/{branch}";
        try
        {
            Track();
            _repo.Network.Push(_repo.Network.Remotes[RemoteName], spec, PushSettings(credentials));
            return new SyncOutcome(true, "Backed up to GitHub.");
        }
        catch (NonFastForwardException ex)
        {
            throw new BookVersioningException(
                "GitHub holds changes this computer has not got. Choose “Get changes from GitHub” first, then back up again.", ex);
        }
        catch (LibGit2SharpException ex)
        {
            throw new BookVersioningException(Friendly(ex, "The book could not be backed up"), ex);
        }
    }

    /// <summary>
    /// Brings in versions saved on another computer. Fast-forwards where this computer has
    /// nothing new, merges where it has, and where the same thing was changed in both places it
    /// changes nothing and says so — the two "keep" methods below settle that.
    /// </summary>
    public SyncOutcome Pull(GitHubCredentials credentials, VersionAuthor author)
    {
        if (!HasRemote) throw new BookVersioningException("This book is not connected to GitHub yet.");
        var before = _repo.Head.Tip;

        try
        {
            Track();
            var signature = new Signature(author.Name, author.Email, DateTimeOffset.Now);
            var result = Commands.Pull(_repo, signature, new PullOptions
            {
                FetchOptions = new FetchOptions { CredentialsProvider = Credentials(credentials) },
                MergeOptions = new MergeOptions
                {
                    FastForwardStrategy  = FastForwardStrategy.Default,
                    CommitOnSuccess      = true,
                    FileConflictStrategy = CheckoutFileConflictStrategy.Normal
                }
            });

            if (result.Status == MergeStatus.Conflicts)
            {
                if (before is not null) _repo.Reset(ResetMode.Hard, before);   // leave the book exactly as it was
                return new SyncOutcome(false,
                    "The same part of the book was changed here and on GitHub, so nothing on this computer was touched. " +
                    "Choose which copy to keep.", Conflict: true);
            }
            if (result.Status == MergeStatus.UpToDate) return new SyncOutcome(false, "Already up to date with GitHub.");
            return new SyncOutcome(true, "Brought in the changes from GitHub. Reopen the book to see them.");
        }
        catch (LibGit2SharpException ex)
        {
            throw new BookVersioningException(Friendly(ex, "The changes could not be fetched"), ex);
        }
    }

    /// <summary>Settles a clash by making GitHub match this computer.</summary>
    public SyncOutcome KeepThisComputer(GitHubCredentials credentials) =>
        Push(credentials, force: true) with { Message = "GitHub now matches this computer." };

    /// <summary>
    /// Settles a clash the other way: this computer takes GitHub's copy. What was here is kept as
    /// a side branch (<c>kept-from-this-computer-…</c>), so nothing written here is ever lost.
    /// </summary>
    public SyncOutcome KeepGitHub(GitHubCredentials credentials)
    {
        if (!HasRemote) throw new BookVersioningException("This book is not connected to GitHub yet.");
        try
        {
            Commands.Fetch(_repo, RemoteName, Array.Empty<string>(),
                new FetchOptions { CredentialsProvider = Credentials(credentials) }, null);
            var remote = _repo.Branches[$"{RemoteName}/{_repo.Head.FriendlyName}"]
                         ?? throw new BookVersioningException("GitHub has no copy of this book yet — back it up first.");

            if (_repo.Head.Tip is { } tip)
                _repo.CreateBranch("kept-from-this-computer-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm"), tip);
            _repo.Reset(ResetMode.Hard, remote.Tip);
            return new SyncOutcome(true, "This computer now matches GitHub. What was here is kept as a side branch.");
        }
        catch (LibGit2SharpException ex)
        {
            throw new BookVersioningException(Friendly(ex, "GitHub's copy could not be taken"), ex);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Track()
    {
        var branch = _repo.Head;
        if (!branch.IsTracking)
            _repo.Branches.Update(branch, b => { b.Remote = RemoteName; b.UpstreamBranch = branch.CanonicalName; });
    }

    private static CredentialsHandler Credentials(GitHubCredentials credentials) =>
        (_, _, _) => new UsernamePasswordCredentials { Username = credentials.Login, Password = credentials.Token };

    private static PushOptions PushSettings(GitHubCredentials credentials) => new()
    {
        CredentialsProvider = Credentials(credentials),
        OnPushStatusError = error => throw new BookVersioningException($"GitHub refused the backup: {error.Message}")
    };

    /// <summary>libgit2's message, read for the things that actually go wrong, in plain English.</summary>
    private static string Friendly(LibGit2SharpException ex, string what)
    {
        var message = ex.Message;
        if (message.Contains("401", StringComparison.Ordinal) ||
            message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("credentials", StringComparison.OrdinalIgnoreCase))
            return $"{what}: GitHub did not accept the sign-in. The token may have expired, or it may not cover this repository — sign in again.";
        if (message.Contains("could not resolve host", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return $"{what}: GitHub could not be reached. Check the internet connection and try again.";
        return $"{what}: {message}";
    }

    private Commit Lookup(string sha) =>
        _repo.Lookup<Commit>(sha) ?? throw new BookVersioningException("That version is not in this book's history any more.");

    private static BookVersion Describe(Commit commit) =>
        new(commit.Sha, commit.Author.When, commit.Author.Name, commit.MessageShort);

    private static string Shorten(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 60 ? line[..57] + "…" : line;
    }

    /// <summary>Chapters first, then the rest of the book, then the bookkeeping files.</summary>
    private static List<BookChange> Sorted(List<BookChange> changes) =>
        changes.OrderBy(c => c.Path.StartsWith("chapters/", StringComparison.OrdinalIgnoreCase) ? 0
                           : c.Path.StartsWith('.') || c.Path == "book.json" ? 2 : 1)
               .ThenBy(c => c.Description, StringComparer.CurrentCultureIgnoreCase)
               .ToList();

    public void Dispose() => _repo.Dispose();
}

/// <summary>
/// What a backup or a fetch did, in one sentence the status bar can show. <paramref name="Conflict"/>
/// is the one case that needs the author to decide: the same part of the book was changed here and
/// on GitHub, and nothing has been touched until they say which copy to keep.
/// </summary>
public sealed record SyncOutcome(bool Changed, string Message, bool Conflict = false);
