using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuthorPlus.AI;

/// <summary>
/// Reads the AI provider API keys PreseMaker stores in <c>Documents\PreseMaker\app_settings.json</c>,
/// so a user who has already configured AI there can opt in to the same keys here
/// (AI Settings → "Use API keys saved in PreseMaker"). One-way and read-only.
///
/// PreseMaker encrypts each key with Windows DPAPI (<see cref="ProtectedData"/>, CurrentUser
/// scope, no entropy) and stores the ciphertext as Base64 in <c>ClaudeApiKeyEncrypted</c> etc.
/// Any app running as the same Windows user can decrypt them. Borrowed keys are used in
/// memory only (<see cref="CreateEffectiveSettings"/>) and never written to AuthorPlus's file.
/// Same approach as CIATLE (see CIATLE\PRESEMAKER_INTEGRATION_PLAN.md).
/// </summary>
public static class PreseMakerCredentials
{
    /// <summary>Test hook: overrides the settings-file location. Null = the real PreseMaker path.</summary>
    internal static string? SettingsPathOverride;

    public static string SettingsPath =>
        SettingsPathOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PreseMaker", "app_settings.json");

    public static bool IsInstalled => File.Exists(SettingsPath);

    public sealed class CredentialSet
    {
        public string ClaudeApiKey  { get; init; } = string.Empty;
        public string GeminiApiKey  { get; init; } = string.Empty;
        public string OpenAiApiKey  { get; init; } = string.Empty;
        public string MistralApiKey { get; init; } = string.Empty;

        public string KeyFor(AiProviderType p) => p switch
        {
            AiProviderType.Claude  => ClaudeApiKey,
            AiProviderType.Gemini  => GeminiApiKey,
            AiProviderType.OpenAi  => OpenAiApiKey,
            AiProviderType.Mistral => MistralApiKey,
            _                      => string.Empty
        };

        public bool HasKey(AiProviderType p) => !string.IsNullOrWhiteSpace(KeyFor(p));

        public IReadOnlyList<AiProviderType> ProvidersWithKeys =>
            AiProviderRouter.AllProviders.Where(HasKey).ToList();
    }

    /// <summary>Loads and decrypts PreseMaker's keys. Null when absent or unreadable; never throws.</summary>
    public static CredentialSet? TryLoad()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string Prop(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            return new CredentialSet
            {
                ClaudeApiKey  = Decrypt(Prop("ClaudeApiKeyEncrypted")),
                GeminiApiKey  = Decrypt(Prop("GeminiApiKeyEncrypted")),
                OpenAiApiKey  = Decrypt(Prop("OpenAiApiKeyEncrypted")),
                MistralApiKey = Decrypt(Prop("MistralApiKeyEncrypted"))
            };
        }
        catch { return null; }
    }

    /// <summary>
    /// A copy of <paramref name="local"/> with PreseMaker's keys filled into blank slots when
    /// sharing is on. Keys typed into AuthorPlus always win. For building providers only —
    /// never persist the result.
    /// </summary>
    public static AiSettings CreateEffectiveSettings(AiSettings local)
    {
        var effective = local.Clone();
        if (!local.UsePreseMakerKeys) return effective;

        var pm = TryLoad();
        if (pm == null) return effective;

        foreach (var p in AiProviderRouter.AllProviders)
            if (string.IsNullOrWhiteSpace(effective.KeyFor(p)) && pm.HasKey(p))
                effective.SetKey(p, pm.KeyFor(p));

        return effective;
    }

    internal static string Decrypt(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64) || !OperatingSystem.IsWindows()) return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(base64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return string.Empty; }
    }
}
