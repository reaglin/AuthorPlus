using System.IO.Compression;
using System.Text;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services.Import;

namespace AuthorPlus.Core.Services.Export;

/// <summary>
/// EPUB 3, written by hand as a zip — the mirror of <see cref="DocxWriter"/> and for the same
/// reason: an EPUB is a zip of XHTML files, so no library is needed to make a valid one.
///
/// One XHTML file per chapter (readers page and cache better that way), a title page, a page
/// per section, one stylesheet, and both tables of contents: <c>nav.xhtml</c> for EPUB 3 and
/// <c>toc.ncx</c> for the older readers that still look for it. The first zip entry is an
/// uncompressed <c>mimetype</c>, which is what makes a zip an EPUB.
/// </summary>
public static class EpubWriter
{
    /// <summary>One XHTML file in the spine: a title page, a section page or a chapter.</summary>
    private sealed record Page(string File, string Id, string NavTitle, Section? Section, bool IsSectionPage);

    public static void Write(ExportSource src, string path)
    {
        var pages = Pages(src);
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        // Must be first and stored uncompressed — the one structural rule of the container.
        Add(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Add(zip, "META-INF/container.xml", Container);
        Add(zip, "OEBPS/style.css", Css);
        Add(zip, "OEBPS/package.opf", Opf(src, pages));
        Add(zip, "OEBPS/nav.xhtml", Nav(src, pages));
        Add(zip, "OEBPS/toc.ncx", Ncx(src, pages));

        Add(zip, "OEBPS/" + pages[0].File, TitlePage(src.Book));
        int i = 1;
        Section? lastSection = null;
        foreach (var (section, ch, n) in src.Walk())
        {
            if (src.SectionHeadings && section is not null && !ReferenceEquals(section, lastSection))
            {
                Add(zip, "OEBPS/" + pages[i].File, SectionPage(section));
                lastSection = section;
                i++;
            }
            Add(zip, "OEBPS/" + pages[i].File, ChapterPage(src, ch, n));
            i++;
        }
    }

    /// <summary>The spine in reading order: title page, then a page per section and per chapter.</summary>
    private static List<Page> Pages(ExportSource src)
    {
        var pages = new List<Page> { new("title.xhtml", "title", src.Book.Title, null, false) };
        Section? lastSection = null;
        int c = 0, s = 0;
        foreach (var (section, ch, n) in src.Walk())
        {
            if (src.SectionHeadings && section is not null && !ReferenceEquals(section, lastSection))
            {
                s++;
                pages.Add(new Page($"part{s:000}.xhtml", $"part{s:000}", section.Title, section, true));
                lastSection = section;
            }
            c++;
            pages.Add(new Page($"chap{c:000}.xhtml", $"chap{c:000}", ChapterHeading(src, ch, n), section, false));
        }
        return pages;
    }

    private static string ChapterHeading(ExportSource src, Chapter ch, int n) =>
        (src.NumberChapters ? $"Chapter {n}: " : "") + ch.Title;

    // ── The documents ─────────────────────────────────────────────────────────

    private static string TitlePage(Book b)
    {
        var sb = new StringBuilder();
        sb.Append("<h1 class=\"book\">").Append(E(b.Title)).Append("</h1>\n");
        if (b.Author.Length > 0) sb.Append("<p class=\"author\">").Append(E(b.Author)).Append("</p>\n");
        return Xhtml(b.Title, sb.ToString());
    }

    private static string SectionPage(Section section)
    {
        // Only the part's title: a section's Summary and Notes are the author's planning, not the book.
        return Xhtml(section.Title, "<h1 class=\"part\">" + E(section.Title) + "</h1>\n");
    }

    private static string ChapterPage(ExportSource src, Chapter ch, int n)
    {
        var heading = ChapterHeading(src, ch, n);
        var sb = new StringBuilder();
        sb.Append("<h1 class=\"chapter\">").Append(E(heading)).Append("</h1>\n");

        bool firstOfBlock = true;                       // no indent on the paragraph that opens a block
        foreach (var p in src.Bodies.TryGetValue(ch.Id, out var d) ? d.Paragraphs : new List<ImportedParagraph>())
        {
            if (p.IsEmpty) continue;
            if (FlowDocumentXaml.IsSceneBreak(p))
            {
                sb.Append("<p class=\"sb\">* * *</p>\n");
                firstOfBlock = true;
                continue;
            }
            if (p.HeadingLevel > 0)
            {
                sb.Append("<h2>").Append(E(p.Text.Trim())).Append("</h2>\n");
                firstOfBlock = true;
                continue;
            }
            sb.Append(firstOfBlock ? "<p class=\"first\">" : "<p>");
            foreach (var r in p.Runs) Inline(sb, r);
            sb.Append("</p>\n");
            firstOfBlock = false;
        }
        return Xhtml(heading, sb.ToString());
    }

    private static void Inline(StringBuilder sb, ImportedRun r)
    {
        var text = E(r.Text).Replace("\n", "<br/>");
        if (r.Underline) text = "<span class=\"u\">" + text + "</span>";
        if (r.Italic) text = "<em>" + text + "</em>";
        if (r.Bold) text = "<strong>" + text + "</strong>";
        sb.Append(text);
    }

    private static string Xhtml(string title, string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\" xml:lang=\"en\">\n" +
        "<head><meta charset=\"utf-8\"/><title>" + E(title) + "</title>" +
        "<link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/></head>\n" +
        "<body>\n" + body + "</body>\n</html>\n";

    // ── The package: manifest, spine, both tables of contents ─────────────────

    private static string Opf(ExportSource src, List<Page> pages)
    {
        var b = src.Book;
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"book-id\" xml:lang=\"en\">\n");
        sb.Append("<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n");
        sb.Append("<dc:identifier id=\"book-id\">urn:uuid:").Append(b.Id).Append("</dc:identifier>\n");
        sb.Append("<dc:title>").Append(E(b.Title)).Append("</dc:title>\n");
        sb.Append("<dc:language>en</dc:language>\n");
        if (b.Author.Length > 0) sb.Append("<dc:creator>").Append(E(b.Author)).Append("</dc:creator>\n");
        if (b.Synopsis.Length > 0) sb.Append("<dc:description>").Append(E(b.Synopsis)).Append("</dc:description>\n");
        if (b.Genre.Length > 0) sb.Append("<dc:subject>").Append(E(b.Genre)).Append("</dc:subject>\n");
        sb.Append("<meta property=\"dcterms:modified\">").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append("</meta>\n");
        sb.Append("</metadata>\n<manifest>\n");
        sb.Append("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n");
        sb.Append("<item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>\n");
        sb.Append("<item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>\n");
        foreach (var p in pages)
            sb.Append("<item id=\"").Append(p.Id).Append("\" href=\"").Append(p.File).Append("\" media-type=\"application/xhtml+xml\"/>\n");
        sb.Append("</manifest>\n<spine toc=\"ncx\">\n");
        foreach (var p in pages) sb.Append("<itemref idref=\"").Append(p.Id).Append("\"/>\n");
        sb.Append("</spine>\n</package>\n");
        return sb.ToString();
    }

    private static string Nav(ExportSource src, List<Page> pages)
    {
        var sb = new StringBuilder();
        sb.Append("<nav epub:type=\"toc\" id=\"toc\"><h1>Contents</h1>\n<ol>\n");
        Section? open = null;
        foreach (var p in pages.Skip(1))
        {
            // Chapters nest under their part; a chapter belonging to no part (or to a different
            // one) closes it, so the book's loose chapters stay at the top level.
            if (open is not null && !ReferenceEquals(p.Section, open)) { sb.Append("</ol></li>\n"); open = null; }
            if (p.IsSectionPage)
            {
                sb.Append("<li><a href=\"").Append(p.File).Append("\">").Append(E(p.NavTitle)).Append("</a>\n<ol>\n");
                open = p.Section;
                continue;
            }
            sb.Append("<li><a href=\"").Append(p.File).Append("\">").Append(E(p.NavTitle)).Append("</a></li>\n");
        }
        if (open is not null) sb.Append("</ol></li>\n");
        sb.Append("</ol>\n</nav>\n");
        return Xhtml("Contents", sb.ToString());
    }

    /// <summary>The EPUB 2 table of contents, kept so older readers still show one.</summary>
    private static string Ncx(ExportSource src, List<Page> pages)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">\n<head>\n");
        sb.Append("<meta name=\"dtb:uid\" content=\"urn:uuid:").Append(src.Book.Id).Append("\"/>\n");
        sb.Append("<meta name=\"dtb:depth\" content=\"").Append(pages.Any(p => p.IsSectionPage) ? 2 : 1).Append("\"/>\n");
        sb.Append("<meta name=\"dtb:totalPageCount\" content=\"0\"/>\n<meta name=\"dtb:maxPageNumber\" content=\"0\"/>\n</head>\n");
        sb.Append("<docTitle><text>").Append(E(src.Book.Title)).Append("</text></docTitle>\n<navMap>\n");
        int order = 0;
        Section? open = null;
        foreach (var p in pages.Skip(1))
        {
            if (open is not null && !ReferenceEquals(p.Section, open)) { sb.Append("</navPoint>\n"); open = null; }
            if (p.IsSectionPage)
            {
                sb.Append(NavPoint(p, ++order));
                open = p.Section;
                continue;
            }
            sb.Append(NavPoint(p, ++order)).Append("</navPoint>\n");
        }
        if (open is not null) sb.Append("</navPoint>\n");
        sb.Append("</navMap>\n</ncx>\n");
        return sb.ToString();
    }

    private static string NavPoint(Page p, int order) =>
        $"<navPoint id=\"nav{p.Id}\" playOrder=\"{order}\"><navLabel><text>{E(p.NavTitle)}</text></navLabel><content src=\"{p.File}\"/>";

    // ── Plumbing ──────────────────────────────────────────────────────────────

    private static void Add(ZipArchive zip, string name, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var e = zip.CreateEntry(name, level);
        using var s = e.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    private static string E(string s) => FlowDocumentXaml.Escape(s);

    private const string Container =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n" +
        "<rootfiles><rootfile full-path=\"OEBPS/package.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles>\n" +
        "</container>\n";

    private const string Css = """
        body       { font-family: Georgia, "Times New Roman", serif; line-height: 1.5; margin: 0 5%; }
        h1.book    { font-size: 2em;   text-align: center; margin-top: 25%; font-weight: bold; }
        p.author   { font-size: 1.1em; text-align: center; font-style: italic; }
        h1.part    { font-size: 1.8em; text-align: center; margin-top: 20%; page-break-before: always; }
        h1.chapter { font-size: 1.4em; margin: 1.5em 0 1em; page-break-before: always; }
        h2         { font-size: 1.1em; margin: 1.2em 0 0.6em; }
        p          { margin: 0; text-indent: 1.2em; text-align: justify; }
        p.first    { text-indent: 0; }
        p.sb       { text-indent: 0; text-align: center; margin: 1em 0; }
        .u         { text-decoration: underline; }
        """;
}
