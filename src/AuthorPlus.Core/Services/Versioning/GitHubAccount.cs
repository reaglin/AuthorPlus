using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuthorPlus.Core.Services.Versioning;

/// <summary>The GitHub sign-in a push or a fetch is made with: the account name and its token.</summary>
public sealed record GitHubCredentials(string Login, string Token);

/// <summary>
/// The one GitHub sign-in this computer remembers, in
/// <c>Documents\AuthorPlus\github.json</c> — beside the book library, not inside a book, because
/// it belongs to the person and not to the manuscript (and so it is never pushed anywhere).
///
/// The token is encrypted with Windows DPAPI under the signed-in user, byte for byte the scheme
/// <c>Eaglin.AiManager</c> keeps API keys with: another user on the same machine cannot read it,
/// and a copy of the file taken to another machine is worthless. On anything but Windows
/// (Core's tests run anywhere) the value is Base64 only, and nothing real runs there.
/// </summary>
public sealed class GitHubAccount
{
    private const int Schema = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AuthorPlus", "github.json");

    public string FilePath { get; }
    public string Login { get; private set; } = string.Empty;
    public DateTime? SavedUtc { get; private set; }

    private string _token = string.Empty;

    private GitHubAccount(string path) => FilePath = path;

    public bool IsSignedIn => Login.Length > 0 && _token.Length > 0;

    /// <summary>What a push or fetch needs, or null when nobody has signed in on this computer.</summary>
    public GitHubCredentials? Credentials => IsSignedIn ? new GitHubCredentials(Login, _token) : null;

    /// <summary>Never throws: an unreadable file is simply "not signed in".</summary>
    public static GitHubAccount Load(string? path = null)
    {
        var account = new GitHubAccount(path ?? DefaultPath);
        try
        {
            if (!File.Exists(account.FilePath)) return account;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(account.FilePath), Json);
            if (stored is null) return account;
            account.Login = stored.Login ?? string.Empty;
            account.SavedUtc = stored.SavedUtc;
            account._token = Unprotect(stored.TokenEncrypted ?? string.Empty);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new GitHubAccount(account.FilePath);
        }
        return account;
    }

    public void Save(string login, string token)
    {
        Login = login.Trim();
        _token = token.Trim();
        SavedUtc = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored
        {
            SchemaVersion = Schema,
            Login = Login,
            TokenEncrypted = Protect(_token),
            SavedUtc = SavedUtc
        }, Json));
    }

    /// <summary>Signs out: the token leaves the computer, the books and their history stay.</summary>
    public void Forget()
    {
        Login = string.Empty;
        _token = string.Empty;
        SavedUtc = null;
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (IOException) { /* it will be overwritten at the next sign-in */ }
    }

    // ── DPAPI, the AiManager scheme ───────────────────────────────────────────

    private static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows()) bytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (OperatingSystem.IsWindows()) bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return string.Empty;   // written by another user, or not a token at all
        }
    }

    private sealed class Stored
    {
        public int       SchemaVersion  { get; set; } = Schema;
        public string?   Login          { get; set; }
        public string?   TokenEncrypted { get; set; }
        public DateTime? SavedUtc       { get; set; }
    }
}
