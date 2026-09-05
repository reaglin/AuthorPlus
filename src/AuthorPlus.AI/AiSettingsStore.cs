using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthorPlus.AI;

/// <summary>
/// Persists <see cref="AiSettings"/> to <c>%APPDATA%\AuthorPlus\ai_settings.json</c> with every
/// API key encrypted by Windows DPAPI (CurrentUser scope) — the same scheme PreseMaker uses, so
/// keys are never on disk in plain text. On non-Windows (tests on CI) keys are stored Base64
/// only, which is fine because nothing real is ever run there.
/// </summary>
public sealed class AiSettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuthorPlus", "ai_settings.json");

    public string Path_ { get; }

    public AiSettingsStore(string? path = null) => Path_ = path ?? DefaultPath;

    public AiSettings Load()
    {
        try
        {
            if (!File.Exists(Path_)) return new AiSettings();
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(Path_, Encoding.UTF8), Json);
            if (dto == null) return new AiSettings();

            var s = new AiSettings
            {
                SelectedProvider  = dto.SelectedProvider,
                UsePreseMakerKeys = dto.UsePreseMakerKeys,
                ClaudeApiKey      = Unprotect(dto.ClaudeApiKeyEncrypted),
                GeminiApiKey      = Unprotect(dto.GeminiApiKeyEncrypted),
                OpenAiApiKey      = Unprotect(dto.OpenAiApiKeyEncrypted),
                MistralApiKey     = Unprotect(dto.MistralApiKeyEncrypted)
            };
            if (!string.IsNullOrWhiteSpace(dto.ClaudeModel))  s.ClaudeModel  = dto.ClaudeModel;
            if (!string.IsNullOrWhiteSpace(dto.GeminiModel))  s.GeminiModel  = dto.GeminiModel;
            if (!string.IsNullOrWhiteSpace(dto.OpenAiModel))  s.OpenAiModel  = dto.OpenAiModel;
            if (!string.IsNullOrWhiteSpace(dto.MistralModel)) s.MistralModel = dto.MistralModel;
            return s;
        }
        catch (Exception ex)
        {
            ActivityLog.Error("AiSettingsStore", "Could not read settings; using defaults", ex);
            return new AiSettings();
        }
    }

    public void Save(AiSettings s)
    {
        var dto = new Dto
        {
            SelectedProvider       = s.SelectedProvider,
            UsePreseMakerKeys      = s.UsePreseMakerKeys,
            ClaudeApiKeyEncrypted  = Protect(s.ClaudeApiKey),
            ClaudeModel            = s.ClaudeModel,
            GeminiApiKeyEncrypted  = Protect(s.GeminiApiKey),
            GeminiModel            = s.GeminiModel,
            OpenAiApiKeyEncrypted  = Protect(s.OpenAiApiKey),
            OpenAiModel            = s.OpenAiModel,
            MistralApiKeyEncrypted = Protect(s.MistralApiKey),
            MistralModel           = s.MistralModel
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
        var tmp = Path_ + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dto, Json), Encoding.UTF8);
        File.Move(tmp, Path_, overwrite: true);
    }

    internal static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows())
            bytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    internal static string Unprotect(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (OperatingSystem.IsWindows())
                bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return string.Empty; }
    }

    private sealed class Dto
    {
        public AiProviderType SelectedProvider       { get; set; } = AiProviderType.Claude;
        public bool           UsePreseMakerKeys      { get; set; }
        public string         ClaudeApiKeyEncrypted  { get; set; } = string.Empty;
        public string         ClaudeModel            { get; set; } = string.Empty;
        public string         GeminiApiKeyEncrypted  { get; set; } = string.Empty;
        public string         GeminiModel            { get; set; } = string.Empty;
        public string         OpenAiApiKeyEncrypted  { get; set; } = string.Empty;
        public string         OpenAiModel            { get; set; } = string.Empty;
        public string         MistralApiKeyEncrypted { get; set; } = string.Empty;
        public string         MistralModel           { get; set; } = string.Empty;
    }
}
