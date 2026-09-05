namespace AuthorPlus.AI;

/// <summary>
/// Contract every AI provider satisfies. Each provider wraps its own REST API and presents
/// one prompt-in / text-out call, so the application never depends on a vendor SDK.
/// </summary>
public interface IAiProvider
{
    /// <summary>Human-readable name, e.g. "Claude (Anthropic)".</summary>
    string ProviderName { get; }

    /// <summary>Model identifier this instance was built with.</summary>
    string Model { get; }

    /// <summary>
    /// Sends a system + user prompt pair and returns the text response.
    /// Throws <see cref="InvalidOperationException"/> on API or auth errors, and
    /// <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    Task<string> GenerateAsync(
        string            systemPrompt,
        string            userPrompt,
        CancellationToken cancellationToken,
        int               maxTokens = 8000);

    /// <summary>True if a non-empty API key is configured (presence only, not validity).</summary>
    bool HasApiKey { get; }
}
