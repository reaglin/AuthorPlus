namespace AuthorPlus.AI;

/// <summary>Supported AI providers. One provider handles every AI task in the app.</summary>
public enum AiProviderType
{
    None    = 0,
    Claude  = 1,   // Anthropic
    Gemini  = 2,   // Google
    OpenAi  = 3,
    Mistral = 4
}

/// <summary>
/// AI provider configuration. Persisted by <see cref="AiSettingsStore"/>, which encrypts the
/// API keys with Windows DPAPI before writing — this in-memory object always holds plaintext.
/// </summary>
public sealed class AiSettings
{
    public AiProviderType SelectedProvider { get; set; } = AiProviderType.Claude;

    /// <summary>
    /// When true, any provider whose key is blank here borrows the key PreseMaker has saved on
    /// this computer (read-only, decrypted in memory only — <see cref="PreseMakerCredentials"/>).
    /// Keys typed into AuthorPlus always win.
    /// </summary>
    public bool UsePreseMakerKeys { get; set; }

    public string ClaudeApiKey  { get; set; } = string.Empty;
    public string ClaudeModel   { get; set; } = AiProviderRouter.DefaultModels[AiProviderType.Claude];

    public string GeminiApiKey  { get; set; } = string.Empty;
    public string GeminiModel   { get; set; } = AiProviderRouter.DefaultModels[AiProviderType.Gemini];

    public string OpenAiApiKey  { get; set; } = string.Empty;
    public string OpenAiModel   { get; set; } = AiProviderRouter.DefaultModels[AiProviderType.OpenAi];

    public string MistralApiKey { get; set; } = string.Empty;
    public string MistralModel  { get; set; } = AiProviderRouter.DefaultModels[AiProviderType.Mistral];

    public string KeyFor(AiProviderType p) => p switch
    {
        AiProviderType.Claude  => ClaudeApiKey,
        AiProviderType.Gemini  => GeminiApiKey,
        AiProviderType.OpenAi  => OpenAiApiKey,
        AiProviderType.Mistral => MistralApiKey,
        _                      => string.Empty
    };

    public void SetKey(AiProviderType p, string key)
    {
        switch (p)
        {
            case AiProviderType.Claude:  ClaudeApiKey  = key; break;
            case AiProviderType.Gemini:  GeminiApiKey  = key; break;
            case AiProviderType.OpenAi:  OpenAiApiKey  = key; break;
            case AiProviderType.Mistral: MistralApiKey = key; break;
        }
    }

    public string ModelFor(AiProviderType p) => p switch
    {
        AiProviderType.Claude  => ClaudeModel,
        AiProviderType.Gemini  => GeminiModel,
        AiProviderType.OpenAi  => OpenAiModel,
        AiProviderType.Mistral => MistralModel,
        _                      => string.Empty
    };

    public void SetModel(AiProviderType p, string model)
    {
        switch (p)
        {
            case AiProviderType.Claude:  ClaudeModel  = model; break;
            case AiProviderType.Gemini:  GeminiModel  = model; break;
            case AiProviderType.OpenAi:  OpenAiModel  = model; break;
            case AiProviderType.Mistral: MistralModel = model; break;
        }
    }

    /// <summary>True when the selected provider has a key (own or borrowed via effective settings).</summary>
    public bool SelectedProviderHasKey => !string.IsNullOrWhiteSpace(KeyFor(SelectedProvider));

    public AiSettings Clone() => (AiSettings)MemberwiseClone();
}
