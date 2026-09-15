using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services.Versioning;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// The book's copy on GitHub: sign in once with a token, give the repository a name, and from
/// then on it is two buttons — back up what is here, or bring in what was written on another
/// computer.
///
/// Nothing here says "push", "pull", "remote" or "branch". What the author needs to know is that
/// GitHub holds a copy, that it is private unless they say otherwise, and what happens when the
/// same chapter was written in two places on the same day — which is the one case this window
/// stops and asks about.
/// </summary>
public sealed class GitHubWindow : Window
{
    private const string TokenPage = "https://github.com/settings/tokens/new?scopes=repo&description=AuthorPlus";

    private readonly GitHubAccount _account;
    private readonly BookRepository _repository;
    private readonly Book _book;
    private readonly VersionAuthor _author;
    private readonly Func<Task> _saveVersionFirst;
    private readonly Action _reloadBook;

    private readonly StackPanel _body = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly ProgressBar _busy = new() { Height = 3, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private bool _conflict;

    /// <summary>True when anything was sent to or brought from GitHub while the window was open.</summary>
    public bool Changed { get; private set; }

    public GitHubWindow(GitHubAccount account, BookRepository repository, Book book, VersionAuthor author,
                        Func<Task> saveVersionFirst, Action reloadBook)
    {
        _account = account;
        _repository = repository;
        _book = book;
        _author = author;
        _saveVersionFirst = saveVersionFirst;
        _reloadBook = reloadBook;

        Title = "Back Up to GitHub";
        Width = 620; SizeToContent = SizeToContent.Height; MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(_body);
        root.Children.Add(_busy);
        root.Children.Add(_status);

        var close = new Button { Content = "Close", Width = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        root.Children.Add(close);

        Content = root;
        Render();
    }

    // ── What the window shows now ─────────────────────────────────────────────

    private void Render()
    {
        _body.Children.Clear();
        if (!_account.IsSignedIn) { SignIn(); return; }

        Heading($"Signed in to GitHub as {_account.Login}", out var row);
        var signOut = Link("Sign out", () =>
        {
            _account.Forget();
            Say("Signed out. The book and its versions on this computer are untouched.");
            Render();
        }, "Forgets the token on this computer; the book, its versions and the copy on GitHub all stay");
        row.Children.Add(signOut);

        if (_repository.RemoteUrl is { } url) Connected(url);
        else NotConnected();
    }

    private void SignIn()
    {
        _body.Children.Add(new TextBlock
        {
            Text = "GitHub can hold a copy of this book — every version of it — so that a lost computer costs you nothing, " +
                   "and so that you can write on one machine and carry on at another.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });

        _body.Children.Add(new TextBlock { Text = "Sign in with a personal access token", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _body.Children.Add(new TextBlock
        {
            Text = "GitHub does not take a password from an app; it takes a token. Making one is free, takes a minute, " +
                   "and you can revoke it whenever you like. You need a GitHub account first — if you have none, " +
                   "make one at github.com, then come back here.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });

        // The steps, because this is the one part of the feature that happens outside AuthorPlus
        // and the GitHub page is written for programmers.
        Step(1, "Open the GitHub page below. It arrives with the one permission AuthorPlus needs — " +
                "“repo” — already ticked. Sign in to GitHub if it asks.");
        Step(2, "Give the token a name you will recognise later, such as “AuthorPlus on this PC”, " +
                "and choose how long it should last. No expiration means never having to do this again; " +
                "a date means you will be asked for a new one then.");
        Step(3, "Scroll to the bottom and press Generate token.");
        Step(4, "Copy the token GitHub then shows you — it is a long line beginning ghp_, and GitHub " +
                "shows it once only. If you lose it, make another; nothing is harmed.");
        Step(5, "Paste it below and press Sign in. AuthorPlus keeps it on this computer, encrypted for " +
                "your Windows account, and never sends it anywhere but GitHub.");

        _body.Children.Add(Link("Open the GitHub page that makes a token…", () => Browse(TokenPage)));
        _body.Children.Add(new TextBlock
        {
            Text = "If GitHub offers you “Fine-grained tokens” instead, choose Tokens (classic) › Generate new token (classic).",
            Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        });

        // No user-name box: the token tells GitHub who its owner is, and asking would only give
        // the author a second thing to get wrong.
        var token = new PasswordBox { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(6) };
        _body.Children.Add(new TextBlock { Text = "Paste the token here (it stays on this computer, encrypted)", Margin = new Thickness(0, 14, 0, 2) });
        _body.Children.Add(token);

        var sign = new Button { Content = "Sign in", Width = 110, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), FontWeight = FontWeights.SemiBold, IsDefault = true };
        sign.Click += async (_, _) =>
        {
            if (token.Password.Trim().Length == 0) { Say("Paste the token from GitHub first.", error: true); return; }
            await Work("Checking the sign-in with GitHub…", async () =>
            {
                using var api = new GitHubApi();
                var user = await api.SignInAsync(token.Password, CancellationToken.None).ConfigureAwait(true);
                _account.Save(user.Login, token.Password);
                Say($"Signed in as {user.Login}.");
                Render();
            });
        };
        _body.Children.Add(sign);
    }

    private void NotConnected()
    {
        _body.Children.Add(new TextBlock
        {
            Text = $"“{_book.Title}” is not on GitHub yet.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 8)
        });

        var name = new TextBox { Text = GitHubApi.SuggestRepositoryName(_book.Title), Padding = new Thickness(6) };
        _body.Children.Add(new TextBlock { Text = "Name for the repository", Margin = new Thickness(0, 4, 0, 2) });
        _body.Children.Add(name);

        var isPrivate = new CheckBox
        {
            Content = "Private — only you can see it",
            IsChecked = true, Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "An unpublished manuscript on a public repository can be read, copied and indexed by anyone."
        };
        _body.Children.Add(isPrivate);

        var create = new Button { Content = "Create it and back up the book", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), FontWeight = FontWeights.SemiBold, IsDefault = true };
        create.Click += async (_, _) => await CreateAndBackUp(name.Text.Trim(), isPrivate.IsChecked == true);
        _body.Children.Add(create);

        _body.Children.Add(new TextBlock
        {
            Text = "Already have a repository for this book? Give its address instead — https://github.com/you/your-book.git",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 4)
        });
        var url = new TextBox { Padding = new Thickness(6) };
        _body.Children.Add(url);
        var connect = new Button { Content = "Use that one", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        connect.Click += async (_, _) =>
        {
            var address = url.Text.Trim();
            if (!address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Say("That does not look like a GitHub address. It should begin with https://", error: true);
                return;
            }
            _repository.SetRemote(address);
            Render();
            await BackUp();
        };
        _body.Children.Add(connect);
    }

    private void Connected(string url)
    {
        var readable = url.Replace("https://", "", StringComparison.OrdinalIgnoreCase).Replace(".git", "", StringComparison.OrdinalIgnoreCase);
        _body.Children.Add(new TextBlock
        {
            Text = $"“{_book.Title}” is backed up to:", Margin = new Thickness(0, 14, 0, 2), TextWrapping = TextWrapping.Wrap
        });
        _body.Children.Add(Link(readable, () => Browse(url.Replace(".git", "", StringComparison.OrdinalIgnoreCase))));

        var pending = _repository.PendingChanges(_book).Count;
        _body.Children.Add(new TextBlock
        {
            Text = pending == 0
                ? "Everything written here is in a version and ready to send."
                : $"{pending} file{(pending == 1 ? "" : "s")} changed since the last version — backing up saves a version first.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        });

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var backUp = new Button { Content = "Back up now", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.SemiBold, IsDefault = true };
        backUp.Click += async (_, _) => await BackUp();
        buttons.Children.Add(backUp);

        var get = new Button { Content = "Get changes from GitHub", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0),
                               ToolTip = "Brings in versions saved on another computer." };
        get.Click += async (_, _) => await GetChanges();
        buttons.Children.Add(get);
        _body.Children.Add(buttons);

        if (_conflict)
        {
            _body.Children.Add(new TextBlock
            {
                Text = "The same part of the book was written here and on GitHub. Nothing has been changed. Which copy should win?",
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6)
            });
            var choice = new WrapPanel();
            var mine = new Button { Content = "Keep this computer's copy", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
            mine.Click += async (_, _) => await Work("Making GitHub match this computer…", async () =>
            {
                var outcome = await Task.Run(() => _repository.KeepThisComputer(Credentials())).ConfigureAwait(true);
                _conflict = false; Changed = true; Say(outcome.Message); Render();
            });
            var theirs = new Button { Content = "Keep GitHub's copy", Padding = new Thickness(12, 4, 12, 4),
                                      ToolTip = "What is on this computer is kept as a side branch, so nothing is lost." };
            theirs.Click += async (_, _) => await Work("Taking GitHub's copy…", async () =>
            {
                var outcome = await Task.Run(() => _repository.KeepGitHub(Credentials())).ConfigureAwait(true);
                _conflict = false; Changed = true; Say(outcome.Message);
                _reloadBook();
                Render();
            });
            choice.Children.Add(mine);
            choice.Children.Add(theirs);
            _body.Children.Add(choice);
        }
    }

    // ── What the buttons do ───────────────────────────────────────────────────

    private async Task CreateAndBackUp(string name, bool isPrivate)
    {
        if (name.Length == 0) { Say("Give the repository a name.", error: true); return; }

        await Work($"Creating {name} on GitHub…", async () =>
        {
            using var api = new GitHubApi();
            var token = Credentials().Token;
            var repository = await api.FindAsync(token, _account.Login, name, CancellationToken.None).ConfigureAwait(true)
                             ?? await api.CreateAsync(token, name,
                                    $"{_book.Title} — written in AuthorPlus", isPrivate, CancellationToken.None).ConfigureAwait(true);
            _repository.SetRemote(repository.CloneUrl);
            Render();
            await BackUp().ConfigureAwait(true);
        });
    }

    private async Task BackUp()
    {
        await _saveVersionFirst().ConfigureAwait(true);
        await Work("Sending the book to GitHub…", async () =>
        {
            var outcome = await Task.Run(() => _repository.Push(Credentials())).ConfigureAwait(true);
            Changed = true;
            Say(outcome.Message + $"  ({DateTime.Now:HH:mm})");
            Render();
        });
    }

    private async Task GetChanges()
    {
        await _saveVersionFirst().ConfigureAwait(true);
        await Work("Looking for changes on GitHub…", async () =>
        {
            var outcome = await Task.Run(() => _repository.Pull(Credentials(), _author)).ConfigureAwait(true);
            _conflict = outcome.Conflict;
            if (outcome.Changed) { Changed = true; _reloadBook(); }
            Say(outcome.Message, error: outcome.Conflict);
            Render();
        });
    }

    private GitHubCredentials Credentials() =>
        _account.Credentials ?? throw new BookVersioningException("Sign in to GitHub first.");

    // ── Plumbing ──────────────────────────────────────────────────────────────

    /// <summary>Runs one GitHub job with the window disabled and a progress line, and says what went wrong in words.</summary>
    private async Task Work(string what, Func<Task> job)
    {
        _busy.Visibility = Visibility.Visible;
        _body.IsEnabled = false;
        Say(what);
        try
        {
            await job().ConfigureAwait(true);
        }
        catch (BookVersioningException ex)
        {
            Say(ex.Message, error: true);
        }
        finally
        {
            _busy.Visibility = Visibility.Collapsed;
            _body.IsEnabled = true;
        }
    }

    private void Say(string text, bool error = false)
    {
        _status.Text = text;
        _status.Foreground = error ? Brushes.Firebrick : Brushes.Black;
    }

    private void Heading(string text, out StackPanel row)
    {
        row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        _body.Children.Add(row);
    }

    private static TextBlock Link(string text, Action act, string tooltip = "Opens in your browser") =>
        Palette.LinkText(text, act, tooltip);

    /// <summary>One numbered step of making a token, hanging indent so the number stands out.</summary>
    private void Step(int number, string text)
    {
        var row = new Grid { Margin = new Thickness(2, 0, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var index = new TextBlock { Text = number + ".", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Top };
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(body, 1);
        row.Children.Add(index);
        row.Children.Add(body);
        _body.Children.Add(row);
    }

    private static void Browse(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser to open it with; the address is on screen to copy.
        }
    }
}
