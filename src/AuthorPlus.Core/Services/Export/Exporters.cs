using System.IO.Compression;
using System.Text;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services.Import;

namespace AuthorPlus.Core.Services.Export;

/// <summary>What an export walks: the book in reading order, each chapter's prose already loaded.</summary>
public sealed class ExportSource
{
    public required Book Book { get; init; }
    /// <summary>Chapter prose by chapter id, as the neutral document shape (from <see cref="FlowDocumentXaml.Parse"/>).</summary>
    public required IReadOnlyDictionary<Guid, ImportedDocument> Bodies { get; init; }
    /// <summary>Include section headings ("Part 1 — …") when the book has sections.</summary>
    public bool SectionHeadings { get; init; } = true;
    /// <summary>Number chapters ("Chapter 3") ahead of their titles.</summary>
    public bool NumberChapters { get; init; } = true;

    public static ExportSource Load(Book book, BookStore store, bool sectionHeadings = true, bool numberChapters = true)
    {
        var bodies = new Dictionary<Guid, ImportedDocument>();
        foreach (var ch in book.Chapters)
        {
            var xaml = store.LoadChapterBody(book, ch);
            bodies[ch.Id] = string.IsNullOrEmpty(xaml) ? new ImportedDocument() : FlowDocumentXaml.Parse(xaml);
        }
        return new ExportSource { Book = book, Bodies = bodies, SectionHeadings = sectionHeadings, NumberChapters = numberChapters };
    }

    /// <summary>The reading order as (section or null, chapter, number within section).</summary>
    public IEnumerable<(Section? Section, Chapter Chapter, int Number)> Walk()
    {
        foreach (var s in Book.Sections)
        {
            int n = 0;
            foreach (var c in Book.ChaptersOf(s)) yield return (s, c, ++n);
        }
        int m = 0;
        foreach (var c in Book.UnsectionedChapters) yield return (null, c, ++m);
    }
}

/// <summary>Markdown: a title block, "# Part" / "## Chapter" headings, paragraphs with *italic* / **bold**, scene breaks as "* * *".</summary>
public static class MarkdownExporter
{
    public static string Render(ExportSource src)
    {
        var sb = new StringBuilder();
        var b = src.Book;
        sb.Append("# ").Append(b.Title).Append('\n');
        if (b.Author.Length > 0) sb.Append("*by ").Append(b.Author).Append("*\n");
        sb.Append('\n');

        Section? lastSection = null;
        foreach (var (section, ch, n) in src.Walk())
        {
            if (src.SectionHeadings && section is not null && !ReferenceEquals(section, lastSection))
            {
                sb.Append("\n# ").Append(section.Title).Append("\n\n");
                lastSection = section;
            }
            sb.Append("\n## ").Append(src.NumberChapters ? $"Chapter {n}: " : "").Append(ch.Title).Append("\n\n");
            foreach (var p in src.Bodies.TryGetValue(ch.Id, out var d) ? d.Paragraphs : new List<ImportedParagraph>())
            {
                if (p.IsEmpty) continue;
                if (FlowDocumentXaml.IsSceneBreak(p)) { sb.Append("* * *\n\n"); continue; }
                if (p.HeadingLevel > 0) sb.Append(new string('#', Math.Min(6, p.HeadingLevel + 2))).Append(' ');
                foreach (var r in p.Runs)
                {
                    var t = r.Text.Replace("\n", "  \n");
                    if (r.Bold && r.Italic) sb.Append("***").Append(t).Append("***");
                    else if (r.Bold) sb.Append("**").Append(t).Append("**");
                    else if (r.Italic) sb.Append('*').Append(t).Append('*');
                    else sb.Append(t);
                }
                sb.Append("\n\n");
            }
        }
        return sb.ToString();
    }

    public static void Write(ExportSource src, string path) => File.WriteAllText(path, Render(src), new UTF8Encoding(false));
}

/// <summary>
/// A Word document written by hand (zip + WordprocessingML), the mirror of <see cref="DocxReader"/>:
/// a title page, Heading1 for sections, Heading2 for chapters, plain paragraphs with bold /
/// italic / underline runs, page breaks between chapters, styles that Word recognises so the
/// navigation pane and table of contents work. No Office library needed.
/// </summary>
public static class DocxWriter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Write(ExportSource src, string path)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", ContentTypes);
        Add(zip, "_rels/.rels", Rels);
        Add(zip, "word/_rels/document.xml.rels", DocumentRels);
        Add(zip, "word/styles.xml", Styles);
        Add(zip, "docProps/core.xml", Core(src.Book));
        Add(zip, "word/document.xml", Document(src));
    }

    private static string Document(ExportSource src)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<w:document xmlns:w=\"{W}\"><w:body>");
        var b = src.Book;

        Para(sb, "Title", new ImportedRun(b.Title));
        if (b.Author.Length > 0) Para(sb, "Subtitle", new ImportedRun("by " + b.Author));
        PageBreak(sb);

        Section? lastSection = null;
        bool first = true;
        foreach (var (section, ch, n) in src.Walk())
        {
            if (!first) PageBreak(sb);
            first = false;
            if (src.SectionHeadings && section is not null && !ReferenceEquals(section, lastSection))
            {
                Para(sb, "Heading1", new ImportedRun(section.Title));
                lastSection = section;
            }
            Para(sb, "Heading2", new ImportedRun((src.NumberChapters ? $"Chapter {n}: " : "") + ch.Title));
            foreach (var p in src.Bodies.TryGetValue(ch.Id, out var d) ? d.Paragraphs : new List<ImportedParagraph>())
            {
                if (FlowDocumentXaml.IsSceneBreak(p)) { Para(sb, "SceneBreak", new ImportedRun("* * *")); continue; }
                Para(sb, p.HeadingLevel > 0 ? "Heading3" : "BodyText", p.Runs.ToArray());
            }
        }

        sb.Append("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>");
        sb.Append("</w:body></w:document>");
        return sb.ToString();
    }

    private static void Para(StringBuilder sb, string style, params ImportedRun[] runs)
    {
        sb.Append("<w:p><w:pPr><w:pStyle w:val=\"").Append(style).Append("\"/></w:pPr>");
        foreach (var r in runs)
        {
            var parts = r.Text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append("<w:r><w:br/></w:r>");
                if (parts[i].Length == 0) continue;
                sb.Append("<w:r>");
                if (r.Bold || r.Italic || r.Underline)
                {
                    sb.Append("<w:rPr>");
                    if (r.Bold) sb.Append("<w:b/>");
                    if (r.Italic) sb.Append("<w:i/>");
                    if (r.Underline) sb.Append("<w:u w:val=\"single\"/>");
                    sb.Append("</w:rPr>");
                }
                sb.Append("<w:t xml:space=\"preserve\">").Append(FlowDocumentXaml.Escape(parts[i])).Append("</w:t></w:r>");
            }
        }
        sb.Append("</w:p>");
    }

    private static void PageBreak(StringBuilder sb) => sb.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");

    private static void Add(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    private static string Core(Book b) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
        $"<dc:title>{FlowDocumentXaml.Escape(b.Title)}</dc:title><dc:creator>{FlowDocumentXaml.Escape(b.Author)}</dc:creator>" +
        $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created></cp:coreProperties>";

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
        "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
        "</Types>";

    private const string Rels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
        "</Relationships>";

    private const string DocumentRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:styles xmlns:w=\"{W}\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Georgia\" w:hAnsi=\"Georgia\"/><w:sz w:val=\"24\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"0\" w:line=\"360\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"BodyText\"><w:name w:val=\"Body Text\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:firstLine=\"360\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:jc w:val=\"center\"/><w:spacing w:before=\"4000\" w:after=\"240\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"56\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Subtitle\"><w:name w:val=\"Subtitle\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:rPr><w:i/><w:sz w:val=\"28\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:jc w:val=\"center\"/><w:spacing w:before=\"2400\" w:after=\"480\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"40\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:before=\"1200\" w:after=\"480\"/><w:outlineLvl w:val=\"1\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading3\"><w:name w:val=\"heading 3\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"120\"/><w:outlineLvl w:val=\"2\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"26\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"SceneBreak\"><w:name w:val=\"Scene Break\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:jc w:val=\"center\"/><w:spacing w:before=\"240\" w:after=\"240\"/></w:pPr></w:style>" +
        "</w:styles>";
}
