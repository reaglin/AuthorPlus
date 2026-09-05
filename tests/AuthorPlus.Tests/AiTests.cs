using System.Security.Cryptography;
using System.Text;
using AuthorPlus.AI;

namespace AuthorPlus.Tests;

public class AiSettingsStoreTests
{
    [Fact]
    public void Keys_are_never_written_in_plain_text_and_round_trip()
    {
        using var t = new TempDir();
        var store = new AiSettingsStore(Path.Combine(t.Path, "ai_settings.json"));
        var s = new AiSettings
        {
            SelectedProvider = AiProviderType.Gemini,
            UsePreseMakerKeys = true,
            ClaudeApiKey = "sk-ant-secret-123",
            GeminiApiKey = "AIza-secret",
            GeminiModel = "models/gemini-2.5-pro"
        };

        store.Save(s);

        var raw = File.ReadAllText(store.Path_);
        Assert.DoesNotContain("sk-ant-secret-123", raw);
        Assert.DoesNotContain("AIza-secret", raw);
        Assert.Contains("ClaudeApiKeyEncrypted", raw);

        var back = store.Load();
        Assert.Equal(AiProviderType.Gemini, back.SelectedProvider);
        Assert.True(back.UsePreseMakerKeys);
        Assert.Equal("sk-ant-secret-123", back.ClaudeApiKey);
        Assert.Equal("AIza-secret", back.GeminiApiKey);
        Assert.Equal("models/gemini-2.5-pro", back.GeminiModel);
        Assert.Equal(AiProviderRouter.DefaultModels[AiProviderType.Claude], back.ClaudeModel);   // untouched default survives
        Assert.Equal(string.Empty, back.OpenAiApiKey);
    }

    [Fact]
    public void Missing_or_corrupt_file_yields_defaults()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "ai_settings.json");
        Assert.Equal(AiProviderType.Claude, new AiSettingsStore(path).Load().SelectedProvider);

        File.WriteAllText(path, "{ nope");
        var s = new AiSettingsStore(path).Load();
        Assert.Equal(AiProviderType.Claude, s.SelectedProvider);
        Assert.Equal(string.Empty, s.ClaudeApiKey);
    }
}

public class AiProviderRouterTests
{
    [Fact]
    public void GetProvider_refuses_when_the_selected_provider_has_no_key()
    {
        var s = new AiSettings { SelectedProvider = AiProviderType.OpenAi };
        var ex = Assert.Throws<InvalidOperationException>(() => AiProviderRouter.GetProvider(s));
        Assert.Contains("OpenAI", ex.Message);
    }

    [Fact]
    public void GetProvider_builds_each_provider_with_its_model_or_the_default()
    {
        foreach (var p in AiProviderRouter.AllProviders)
        {
            var s = new AiSettings { SelectedProvider = p };
            s.SetKey(p, "key");
            s.SetModel(p, "");
            var provider = AiProviderRouter.GetProvider(s);
            Assert.True(provider.HasApiKey);
            Assert.Equal(AiProviderRouter.DefaultModels[p], provider.Model);

            s.SetModel(p, "custom-model");
            Assert.Equal("custom-model", AiProviderRouter.GetProvider(s).Model);
        }
    }

    [Fact]
    public void Default_Claude_model_is_the_current_generation()
    {
        Assert.Equal("claude-opus-5", AiProviderRouter.DefaultModels[AiProviderType.Claude]);
        Assert.Contains("claude-opus-5", AiProviderRouter.AvailableModels[AiProviderType.Claude]);
    }
}

public class PreseMakerCredentialsTests
{
    private static string Encrypt(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows())
            bytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public void Borrowed_keys_fill_blank_slots_only_when_sharing_is_on_and_never_override_local_keys()
    {
        if (!OperatingSystem.IsWindows()) return;   // DPAPI is Windows-only; nothing to test elsewhere

        using var t = new TempDir();
        var path = Path.Combine(t.Path, "app_settings.json");
        File.WriteAllText(path,
            "{ \"ClaudeApiKeyEncrypted\": \"" + Encrypt("pm-claude") + "\", " +
            "  \"OpenAiApiKeyEncrypted\": \"" + Encrypt("pm-openai") + "\", " +
            "  \"GeminiApiKeyEncrypted\": \"\" }");
        PreseMakerCredentials.SettingsPathOverride = path;
        try
        {
            Assert.True(PreseMakerCredentials.IsInstalled);
            var pm = PreseMakerCredentials.TryLoad();
            Assert.NotNull(pm);
            Assert.Equal(new[] { AiProviderType.Claude, AiProviderType.OpenAi }, pm!.ProvidersWithKeys);

            var local = new AiSettings { UsePreseMakerKeys = false, OpenAiApiKey = "mine" };
            var off = PreseMakerCredentials.CreateEffectiveSettings(local);
            Assert.Equal(string.Empty, off.ClaudeApiKey);

            local.UsePreseMakerKeys = true;
            var on = PreseMakerCredentials.CreateEffectiveSettings(local);
            Assert.Equal("pm-claude", on.ClaudeApiKey);   // blank slot filled
            Assert.Equal("mine", on.OpenAiApiKey);        // local wins
            Assert.Equal(string.Empty, on.GeminiApiKey);  // nothing to borrow
            Assert.Equal(string.Empty, local.ClaudeApiKey);   // the persisted object is untouched
        }
        finally
        {
            PreseMakerCredentials.SettingsPathOverride = null;
        }
    }

    [Fact]
    public void Missing_or_unparseable_file_returns_null_without_throwing()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "app_settings.json");
        PreseMakerCredentials.SettingsPathOverride = path;
        try
        {
            Assert.False(PreseMakerCredentials.IsInstalled);
            Assert.Null(PreseMakerCredentials.TryLoad());
            File.WriteAllText(path, "not json");
            Assert.Null(PreseMakerCredentials.TryLoad());
        }
        finally { PreseMakerCredentials.SettingsPathOverride = null; }
    }
}
