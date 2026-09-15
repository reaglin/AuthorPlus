using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AuthorPlus.Core.Services.Versioning;

/// <summary>The account a token belongs to.</summary>
public sealed record GitHubUser(string Login, string Name);

/// <summary>A repository on GitHub — everything the book folder needs to point at it.</summary>
public sealed record GitHubRepository(string Owner, string Name, string CloneUrl, string HtmlUrl, bool IsPrivate)
{
    public string FullName => $"{Owner}/{Name}";
}

/// <summary>
/// The three things AuthorPlus asks GitHub over its API: who this token belongs to, does this
/// repository exist, and please make one. Everything else — the versions themselves — goes over
/// git, not here.
///
/// Written straight onto <see cref="HttpClient"/> rather than through a GitHub SDK: three calls
/// do not justify a dependency in a Store app, and the errors that matter (a wrong token, a
/// token without the right permission, a name already taken) are clearer said in our own words.
/// </summary>
public sealed class GitHubApi : IDisposable
{
    private const string ApiRoot = "https://api.github.com";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public GitHubApi(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AuthorPlus");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!_http.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <summary>Checks a token and says whose it is.</summary>
    public async Task<GitHubUser> SignInAsync(string token, CancellationToken cancel = default)
    {
        using var response = await Send(HttpMethod.Get, "/user", token, null, cancel).ConfigureAwait(false);
        var root = await Read(response, "The GitHub sign-in could not be checked", cancel).ConfigureAwait(false);
        var login = Text(root, "login");
        if (login.Length == 0) throw new BookVersioningException("GitHub answered without an account name. Try signing in again.");
        return new GitHubUser(login, Text(root, "name") is { Length: > 0 } name ? name : login);
    }

    /// <summary>The repository, or null when there is none of that name.</summary>
    public async Task<GitHubRepository?> FindAsync(string token, string owner, string name, CancellationToken cancel = default)
    {
        using var response = await Send(HttpMethod.Get, $"/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}", token, null, cancel).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var root = await Read(response, "GitHub could not be asked about that repository", cancel).ConfigureAwait(false);
        return ToRepository(root);
    }

    /// <summary>
    /// Creates a repository on the signed-in account. Empty — no README, no licence, no
    /// .gitignore — because the book's own first version is what goes into it, and a repository
    /// that already has a commit would refuse the first backup.
    /// </summary>
    public async Task<GitHubRepository> CreateAsync(string token, string name, string description, bool isPrivate, CancellationToken cancel = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            name,
            description,
            @private = isPrivate,
            auto_init = false,
            has_issues = false,
            has_projects = false,
            has_wiki = false
        });
        using var response = await Send(HttpMethod.Post, "/user/repos", token, body, cancel).ConfigureAwait(false);
        var root = await Read(response, "The repository could not be created", cancel).ConfigureAwait(false);
        return ToRepository(root);
    }

    /// <summary>"The Book of One" → "the-book-of-one": a name GitHub will take.</summary>
    public static string SuggestRepositoryName(string bookTitle)
    {
        var builder = new StringBuilder();
        foreach (var c in bookTitle.Trim())
        {
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var name = builder.ToString().Trim('-');
        if (name.Length > 90) name = name[..90].Trim('-');
        return name.Length == 0 ? "my-book" : name;
    }

    // ── Plumbing ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, string? body, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, ApiRoot + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            return await _http.SendAsync(request, cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new BookVersioningException("GitHub could not be reached. Check the internet connection and try again.", ex);
        }
        catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested)
        {
            throw new BookVersioningException("GitHub did not answer in time. Try again.", ex);
        }
    }

    /// <summary>The body on success; on failure, what went wrong in words the author can act on.</summary>
    private static async Task<JsonElement> Read(HttpResponseMessage response, string what, CancellationToken cancel)
    {
        var text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            try { return JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException ex) { throw new BookVersioningException($"{what}: GitHub sent an answer AuthorPlus could not read.", ex); }
        }

        var detail = Message(text);
        throw new BookVersioningException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => $"{what}: GitHub did not accept that token. Check it was copied whole, and that it has not expired.",
            HttpStatusCode.Forbidden    => $"{what}: that token is not allowed to do this. It needs permission to read and write repository contents (and to create repositories, for a new one).",
            HttpStatusCode.NotFound     => $"{what}: GitHub has nothing of that name, or the token cannot see it.",
            HttpStatusCode.UnprocessableEntity when detail.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                                        => "That account already has a repository with this name. Choose another name, or connect the book to the one that is there.",
            _                           => $"{what}: GitHub said {(int)response.StatusCode} {response.StatusCode}{(detail.Length > 0 ? " — " + detail : "")}."
        });
    }

    private static string Message(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var message = Text(document.RootElement, "message");
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                foreach (var error in errors.EnumerateArray())
                    if (Text(error, "message") is { Length: > 0 } detail) return detail;
            return message;
        }
        catch (JsonException) { return string.Empty; }
    }

    private static GitHubRepository ToRepository(JsonElement root) => new(
        root.TryGetProperty("owner", out var owner) ? Text(owner, "login") : string.Empty,
        Text(root, "name"),
        Text(root, "clone_url"),
        Text(root, "html_url"),
        root.TryGetProperty("private", out var isPrivate) && isPrivate.ValueKind == JsonValueKind.True);

    private static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
