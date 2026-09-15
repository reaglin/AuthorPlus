using System.Net;
using System.Text.Json;
using AuthorPlus.Core.Services.Versioning;

namespace AuthorPlus.Tests;

/// <summary>
/// The three GitHub calls, answered by a stub rather than by GitHub: what AuthorPlus sends, what
/// it makes of the answer, and — the part that matters most — that every failure reaches the
/// author as a sentence they can act on instead of a status code.
/// </summary>
public class GitHubApiTests
{
    private sealed class Stub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _answer;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public Stub(Func<HttpRequestMessage, (HttpStatusCode, string)> answer) => _answer = answer;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var (status, body) = _answer(request);
            return new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static (GitHubApi Api, Stub Stub) Api(Func<HttpRequestMessage, (HttpStatusCode, string)> answer)
    {
        var stub = new Stub(answer);
        return (new GitHubApi(new HttpClient(stub)), stub);
    }

    [Fact]
    public async Task Signing_in_says_who_the_token_belongs_to_and_sends_it_as_a_bearer_token()
    {
        var (api, stub) = Api(_ => (HttpStatusCode.OK, """{"login":"reaglin","name":"Ron Eaglin"}"""));

        var user = await api.SignInAsync("ghp_token");

        Assert.Equal("reaglin", user.Login);
        Assert.Equal("Ron Eaglin", user.Name);
        Assert.Equal("https://api.github.com/user", stub.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer ghp_token", stub.LastRequest.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task A_bad_token_is_reported_in_words_the_author_can_act_on()
    {
        var (api, _) = Api(_ => (HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}"""));

        var ex = await Assert.ThrowsAsync<BookVersioningException>(() => api.SignInAsync("wrong"));

        Assert.Contains("did not accept that token", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("401", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_without_the_right_permission_says_which_permission_is_missing()
    {
        var (api, _) = Api(_ => (HttpStatusCode.Forbidden, """{"message":"Resource not accessible by personal access token"}"""));

        var ex = await Assert.ThrowsAsync<BookVersioningException>(() => api.CreateAsync("t", "book", "", true));

        Assert.Contains("read and write repository contents", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Creating_a_repository_asks_for_a_private_empty_one_and_reads_back_where_it_is()
    {
        var (api, stub) = Api(_ => (HttpStatusCode.Created, """
            {"name":"the-book-of-one","private":true,"clone_url":"https://github.com/reaglin/the-book-of-one.git",
             "html_url":"https://github.com/reaglin/the-book-of-one","owner":{"login":"reaglin"}}
            """));

        var repository = await api.CreateAsync("ghp_token", "the-book-of-one", "The Book of One — written in AuthorPlus", isPrivate: true);

        Assert.Equal("reaglin/the-book-of-one", repository.FullName);
        Assert.Equal("https://github.com/reaglin/the-book-of-one.git", repository.CloneUrl);
        Assert.True(repository.IsPrivate);

        using var sent = JsonDocument.Parse(stub.LastBody!);
        Assert.Equal("the-book-of-one", sent.RootElement.GetProperty("name").GetString());
        Assert.True(sent.RootElement.GetProperty("private").GetBoolean());
        // auto_init would put a README commit in the way of the book's own first version.
        Assert.False(sent.RootElement.GetProperty("auto_init").GetBoolean());
    }

    [Fact]
    public async Task A_name_already_taken_suggests_the_two_ways_out()
    {
        var (api, _) = Api(_ => (HttpStatusCode.UnprocessableEntity,
            """{"message":"Repository creation failed.","errors":[{"message":"name already exists on this account"}]}"""));

        var ex = await Assert.ThrowsAsync<BookVersioningException>(() => api.CreateAsync("t", "taken", "", true));

        Assert.Contains("already has a repository with this name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Looking_for_a_repository_that_is_not_there_is_not_an_error()
    {
        var (api, _) = Api(_ => (HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        Assert.Null(await api.FindAsync("t", "reaglin", "no-such-book"));
    }

    [Fact]
    public async Task GitHub_being_unreachable_is_said_plainly()
    {
        var api = new GitHubApi(new HttpClient(new ThrowingHandler()));

        var ex = await Assert.ThrowsAsync<BookVersioningException>(() => api.SignInAsync("t"));

        Assert.Contains("could not be reached", ex.Message, StringComparison.Ordinal);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No such host is known.");
    }
}
