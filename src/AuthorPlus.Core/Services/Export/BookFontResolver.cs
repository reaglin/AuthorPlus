using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace AuthorPlus.Core.Services.Export;

/// <summary>
/// Supplies font files to PDFsharp for <see cref="PdfBookWriter"/>.
///
/// The core PDFsharp package carries no fonts and resolves none by itself — it throws "No
/// appropriate font found" the first time one is asked for. Taking the GDI or WPF flavour of
/// the package instead would put a UI dependency in <c>AuthorPlus.Core</c> and break the rule
/// that Core runs anywhere, so this reads the font files off the machine directly: file IO,
/// not a UI reference. (The same approach as <c>EasyPeasyRetirement</c>'s report writer.)
///
/// A book wants a serif with all four faces, so families are tried whole: Georgia first
/// (what the editor and the Word export use), then the usual stand-ins on each platform.
/// </summary>
public sealed class BookFontResolver : IFontResolver
{
    /// <summary>The family name to build an <c>XFont</c> with; this resolver maps it to whatever the machine has.</summary>
    public const string BookSerif = "AuthorPlus Serif";

    /// <summary>Families tried in order: regular, bold, italic, bold-italic file names.</summary>
    private static readonly (string Family, string Regular, string Bold, string Italic, string BoldItalic)[] Candidates =
    [
        ("georgia",          "georgia.ttf",  "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf"),
        ("constantia",       "constan.ttf",  "constanb.ttf", "constani.ttf", "constanz.ttf"),
        ("times new roman",  "times.ttf",    "timesbd.ttf",  "timesi.ttf",   "timesbi.ttf"),
        ("dejavu serif",     "DejaVuSerif.ttf",            "DejaVuSerif-Bold.ttf",            "DejaVuSerif-Italic.ttf",            "DejaVuSerif-BoldItalic.ttf"),
        ("liberation serif", "LiberationSerif-Regular.ttf", "LiberationSerif-Bold.ttf",        "LiberationSerif-Italic.ttf",        "LiberationSerif-BoldItalic.ttf"),
        ("times",            "Times New Roman.ttf",         "Times New Roman Bold.ttf",        "Times New Roman Italic.ttf",        "Times New Roman Bold Italic.ttf"),
    ];

    private static readonly string[] SearchFolders =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
        "/usr/share/fonts/truetype/dejavu",
        "/usr/share/fonts/truetype/liberation",
        "/usr/share/fonts/truetype/msttcorefonts",
        "/Library/Fonts",
        "/System/Library/Fonts/Supplemental",
    ];

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();
    private static readonly object InstallLock = new();

    /// <summary>Installs this resolver once, before any font is used. Safe to call repeatedly.</summary>
    public static void Install()
    {
        lock (InstallLock)
        {
            GlobalFontSettings.FontResolver ??= new BookFontResolver();
        }
    }

    /// <summary>The family this machine will actually print with, or null when no serif was found.</summary>
    public static string? AvailableFamily =>
        Candidates.FirstOrDefault(c => Find(c.Regular) is not null).Family;

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        foreach (var c in Candidates)
        {
            if (Find(c.Regular) is null) continue;                 // family not on this machine
            return new FontResolverInfo($"{c.Family}|{Face(isBold, isItalic)}");
        }
        return null;
    }

    public byte[]? GetFont(string faceName)
    {
        var bytes = Cache.GetOrAdd(faceName, name =>
        {
            var parts = name.Split('|');
            var face = parts.Length > 1 ? parts[1] : "r";

            foreach (var c in Candidates)
            {
                if (!string.Equals(c.Family, parts[0], StringComparison.OrdinalIgnoreCase)) continue;
                var wanted = face switch { "b" => c.Bold, "i" => c.Italic, "bi" => c.BoldItalic, _ => c.Regular };
                // Fall back within the family rather than lose the whole export over one missing face.
                var path = Find(wanted) ?? Find(c.Regular);
                if (path is not null) return File.ReadAllBytes(path);
            }

            foreach (var c in Candidates)
                if (Find(c.Regular) is string any) return File.ReadAllBytes(any);

            return [];
        });
        return bytes.Length == 0 ? null : bytes;
    }

    private static string Face(bool bold, bool italic) => bold && italic ? "bi" : bold ? "b" : italic ? "i" : "r";

    private static string? Find(string fileName)
    {
        foreach (var folder in SearchFolders)
        {
            if (string.IsNullOrEmpty(folder)) continue;
            var candidate = Path.Combine(folder, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
