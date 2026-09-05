using AuthorPlus.AI.Providers;

namespace AuthorPlus.AI;

/// <summary>
/// Resolves the <see cref="IAiProvider"/> for the user's <see cref="AiSettings"/> and holds the
/// display names and model lists the settings window shows. One provider handles every task.
/// </summary>
public static class AiProviderRouter
{
    private static readonly Dictionary<AiProviderType, string> DisplayNames = new()
    {
        { AiProviderType.Claude,  "Claude (Anthropic)" },
        { AiProviderType.Gemini,  "Gemini (Google)"    },
        { AiProviderType.OpenAi,  "OpenAI"             },
        { AiProviderType.Mistral, "Mistral AI"         }
    };

    /// <summary>Model chosen when the user has not picked one.</summary>
    public static readonly Dictionary<AiProviderType, string> DefaultModels = new()
    {
        { AiProviderType.Claude,  "claude-opus-5"           },
        { AiProviderType.Gemini,  "models/gemini-2.5-flash" },
        { AiProviderType.OpenAi,  "gpt-4o"                  },
        { AiProviderType.Mistral, "mistral-large-latest"    }
    };

    /// <summary>Choices offered in the settings window; the user may also type any other id.</summary>
    public static readonly Dictionary<AiProviderType, string[]> AvailableModels = new()
    {
        { AiProviderType.Claude, new[]
            { "claude-opus-5", "claude-sonnet-5", "claude-fable-5-1", "claude-haiku-4-5" } },
        { AiProviderType.Gemini, new[]
            { "models/gemini-2.5-flash", "models/gemini-2.5-pro" } },
        { AiProviderType.OpenAi, new[]
            { "gpt-4o", "gpt-4o-mini" } },
        { AiProviderType.Mistral, new[]
            { "mistral-large-latest", "mistral-medium-latest", "mistral-small-latest" } }
    };

    /// <summary>All provider types except None.</summary>
    public static IEnumerable<AiProviderType> AllProviders =>
        Enum.GetValues<AiProviderType>().Where(p => p != AiProviderType.None);

    public static string DisplayName(AiProviderType provider) =>
        DisplayNames.TryGetValue(provider, out var n) ? n : provider.ToString();

    /// <summary>
    /// Builds the selected provider from <paramref name="settings"/>. Pass the *effective*
    /// settings (<see cref="PreseMakerCredentials.CreateEffectiveSettings"/>) so borrowed keys
    /// apply. Throws when the selected provider has no key.
    /// </summary>
    public static IAiProvider GetProvider(AiSettings settings)
    {
        var p = settings.SelectedProvider;
        if (!settings.SelectedProviderHasKey)
            throw new InvalidOperationException(
                $"{DisplayName(p)} has no API key. Add one in AI Settings.");

        var model = string.IsNullOrWhiteSpace(settings.ModelFor(p)) ? DefaultModels[p] : settings.ModelFor(p);
        return p switch
        {
            AiProviderType.Claude  => new ClaudeProvider (settings.ClaudeApiKey,  model),
            AiProviderType.Gemini  => new GeminiProvider (settings.GeminiApiKey,  model),
            AiProviderType.OpenAi  => new OpenAiProvider (settings.OpenAiApiKey,  model),
            AiProviderType.Mistral => new MistralProvider(settings.MistralApiKey, model),
            _ => throw new InvalidOperationException($"Unknown provider: {p}")
        };
    }
}
