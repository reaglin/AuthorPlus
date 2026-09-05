using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthorPlus.AI.Providers;

/// <summary>Shared plumbing: one HttpClient, uniform error mapping, usage logging.</summary>
internal static class ProviderHttp
{
    public static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(300) };

    public static async Task<HttpResponseMessage> SendAsync(
        string context, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await Client.SendAsync(request, ct);
        }
        catch (TaskCanceledException tce) when (!ct.IsCancellationRequested)
        {
            ActivityLog.Error(context, "TIMEOUT", tce);
            throw new InvalidOperationException(
                $"The request timed out after {Client.Timeout.TotalSeconds:0}s.", tce);
        }
        catch (HttpRequestException hre)
        {
            ActivityLog.Error(context, "NETWORK", hre);
            throw new InvalidOperationException($"Network error: {hre.Message}", hre);
        }
    }

    public static async Task ThrowIfFailed(string context, string vendor, HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        var detail = TryExtractErrorMessage(body);
        ActivityLog.Error(context, $"HTTP {(int)response.StatusCode}: {detail}");
        throw new InvalidOperationException($"{vendor} returned {(int)response.StatusCode}: {detail}");
    }

    /// <summary>Pulls "error.message" (all four vendors use that shape) from an error body.</summary>
    private static string TryExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? body;
                if (err.ValueKind == JsonValueKind.String) return err.GetString() ?? body;
            }
        }
        catch { /* not JSON */ }
        return body.Length > 600 ? body[..600] + "…" : body;
    }
}

// ── Claude (Anthropic) ────────────────────────────────────────────────────────

/// <summary>Anthropic Messages API (<c>/v1/messages</c>). Logs token usage and estimated cost.</summary>
public sealed class ClaudeProvider : IAiProvider
{
    public string ProviderName => "Claude (Anthropic)";
    public string Model { get; }

    private readonly string _apiKey;
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    // USD per million tokens for the default model (claude-opus-5); other models differ.
    private const double InputCostPerMillion  = 5.00;
    private const double OutputCostPerMillion = 25.00;

    public ClaudeProvider(string apiKey, string model) { _apiKey = apiKey; Model = model; }
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string> GenerateAsync(
        string systemPrompt, string userPrompt, CancellationToken ct, int maxTokens = 8000)
    {
        if (!HasApiKey)
            throw new InvalidOperationException("Claude API key is not configured. Add it in AI Settings.");

        var context = $"Claude/{Model}";
        ActivityLog.AiCall(context, $"REQUEST  max_tokens={maxTokens}  system_chars={systemPrompt.Length}  user_chars={userPrompt.Length}");

        var request = new
        {
            model      = Model,
            max_tokens = maxTokens,
            system     = systemPrompt,
            messages   = new[] { new { role = "user", content = userPrompt } }
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        http.Headers.Add("x-api-key", _apiKey);
        http.Headers.Add("anthropic-version", AnthropicVersion);
        http.Content = JsonContent.Create(request);

        using var response = await ProviderHttp.SendAsync(context, http, ct);
        await ProviderHttp.ThrowIfFailed(context, "Anthropic", response, ct);

        var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>(cancellationToken: ct)
                     ?? throw new InvalidOperationException("Claude returned an empty response.");

        if (result.StopReason == "refusal")
        {
            ActivityLog.Warn(context, "stop_reason=refusal");
            throw new InvalidOperationException("Claude declined this request (safety refusal). Rephrase and try again.");
        }

        var text = string.Concat(result.Content?.Where(c => c.Type == "text").Select(c => c.Text) ?? Array.Empty<string>());
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException("Claude returned no text.");

        var inTok  = result.Usage?.InputTokens  ?? 0;
        var outTok = result.Usage?.OutputTokens ?? 0;
        var cost   = inTok / 1_000_000.0 * InputCostPerMillion + outTok / 1_000_000.0 * OutputCostPerMillion;
        ActivityLog.AiCall(context, $"RESPONSE  stop={result.StopReason}  in={inTok}  out={outTok}  est_cost=${cost:F5}  chars={text.Length}");
        if (result.StopReason == "max_tokens")
            ActivityLog.Warn(context, $"Output truncated at {outTok} tokens (max_tokens={maxTokens}).");

        return text;
    }

    private sealed record ClaudeResponse(
        [property: JsonPropertyName("content")]     ClaudeContent[]? Content,
        [property: JsonPropertyName("usage")]       ClaudeUsage?     Usage,
        [property: JsonPropertyName("stop_reason")] string?          StopReason);
    private sealed record ClaudeContent(
        [property: JsonPropertyName("type")] string  Type,
        [property: JsonPropertyName("text")] string? Text);
    private sealed record ClaudeUsage(
        [property: JsonPropertyName("input_tokens")]  int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);
}

// ── Gemini (Google) ───────────────────────────────────────────────────────────

public sealed class GeminiProvider : IAiProvider
{
    public string ProviderName => "Gemini (Google)";
    public string Model { get; }
    private readonly string _apiKey;

    public GeminiProvider(string apiKey, string model) { _apiKey = apiKey; Model = model; }
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string> GenerateAsync(
        string systemPrompt, string userPrompt, CancellationToken ct, int maxTokens = 8000)
    {
        if (!HasApiKey)
            throw new InvalidOperationException("Gemini API key is not configured. Add it in AI Settings.");

        var context = $"Gemini/{Model}";
        ActivityLog.AiCall(context, $"REQUEST  max_tokens={maxTokens}  system_chars={systemPrompt.Length}  user_chars={userPrompt.Length}");

        var modelSegment = Model.TrimStart('/');
        if (!modelSegment.StartsWith("models/", StringComparison.Ordinal)) modelSegment = "models/" + modelSegment;
        var url = $"https://generativelanguage.googleapis.com/v1beta/{modelSegment}:generateContent";

        var request = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents          = new[] { new { parts = new[] { new { text = userPrompt } } } },
            generationConfig  = new { maxOutputTokens = maxTokens }
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, url);
        http.Headers.Add("x-goog-api-key", _apiKey);
        http.Content = JsonContent.Create(request);

        using var response = await ProviderHttp.SendAsync(context, http, ct);
        await ProviderHttp.ThrowIfFailed(context, "Gemini", response, ct);

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
        var text = string.Concat(result?.Candidates?.FirstOrDefault()?.Content?.Parts?.Select(p => p.Text) ?? Array.Empty<string>());
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException("Gemini returned an empty response.");

        ActivityLog.AiCall(context, $"RESPONSE  chars={text.Length}");
        return text;
    }

    private sealed record GeminiResponse([property: JsonPropertyName("candidates")] GeminiCandidate[]? Candidates);
    private sealed record GeminiCandidate([property: JsonPropertyName("content")] GeminiContent? Content);
    private sealed record GeminiContent([property: JsonPropertyName("parts")] GeminiPart[]? Parts);
    private sealed record GeminiPart([property: JsonPropertyName("text")] string? Text);
}

// ── OpenAI ────────────────────────────────────────────────────────────────────

public sealed class OpenAiProvider : IAiProvider
{
    public string ProviderName => "OpenAI";
    public string Model { get; }
    private readonly string _apiKey;
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    public OpenAiProvider(string apiKey, string model) { _apiKey = apiKey; Model = model; }
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct, int maxTokens = 8000) =>
        ChatCompletions.GenerateAsync($"OpenAI/{Model}", "OpenAI", ApiUrl, _apiKey, Model, systemPrompt, userPrompt, maxTokens, ct);
}

// ── Mistral ───────────────────────────────────────────────────────────────────

public sealed class MistralProvider : IAiProvider
{
    public string ProviderName => "Mistral AI";
    public string Model { get; }
    private readonly string _apiKey;
    private const string ApiUrl = "https://api.mistral.ai/v1/chat/completions";

    public MistralProvider(string apiKey, string model) { _apiKey = apiKey; Model = model; }
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct, int maxTokens = 8000) =>
        ChatCompletions.GenerateAsync($"Mistral/{Model}", "Mistral", ApiUrl, _apiKey, Model, systemPrompt, userPrompt, maxTokens, ct);
}

/// <summary>OpenAI-style chat-completions call shared by OpenAI and Mistral.</summary>
internal static class ChatCompletions
{
    public static async Task<string> GenerateAsync(
        string context, string vendor, string url, string apiKey, string model,
        string systemPrompt, string userPrompt, int maxTokens, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"{vendor} API key is not configured. Add it in AI Settings.");

        ActivityLog.AiCall(context, $"REQUEST  max_tokens={maxTokens}  system_chars={systemPrompt.Length}  user_chars={userPrompt.Length}");

        var request = new
        {
            model      = model,
            max_tokens = maxTokens,
            messages   = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   }
            }
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, url);
        http.Headers.Add("Authorization", $"Bearer {apiKey}");
        http.Content = JsonContent.Create(request);

        using var response = await ProviderHttp.SendAsync(context, http, ct);
        await ProviderHttp.ThrowIfFailed(context, vendor, response, ct);

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
        var text = result?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException($"{vendor} returned an empty response.");

        ActivityLog.AiCall(context, $"RESPONSE  in={result?.Usage?.PromptTokens ?? 0}  out={result?.Usage?.CompletionTokens ?? 0}  chars={text.Length}");
        return text;
    }

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] ChatChoice[]? Choices,
        [property: JsonPropertyName("usage")]   ChatUsage?    Usage);
    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage? Message);
    private sealed record ChatMessage([property: JsonPropertyName("content")] string? Content);
    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")]     int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
