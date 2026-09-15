using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Export;
using AuthorPlus.Core.Services.Import;

namespace AuthorPlus.Tests;

public class EpubTests
{
    private static Dictionary<string, string> Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            return r.ReadToEnd();
        });
    }

    [Fact]
    public void Container_starts_with_an_uncompressed_mimetype_and_carries_every_required_part()
    {
        using var t = new TempDir();
        var (book, store) = ExportTests.MakeBook(t);
        var path = Path.Combine(t.Path, "out.epub");

        EpubWriter.Write(ExportSource.Load(book, store), path);

        using (var zip = ZipFile.OpenRead(path))
        {
            var first = zip.Entries[0];
            Assert.Equal("mimetype", first.FullName);                       // must be the first entry…
            Assert.Equal(first.Length, first.CompressedLength);             // …and stored, not deflated
        }

        var files = Read(path);
        Assert.Equal("application/epub+zip", files["mimetype"]);
        Assert.Contains("OEBPS/package.opf", files["META-INF/container.xml"]);
        foreach (var required in new[] { "OEBPS/package.opf", "OEBPS/nav.xhtml", "OEBPS/toc.ncx", "OEBPS/style.css", "OEBPS/title.xhtml" })
            Assert.True(files.ContainsKey(required), required + " is missing");

        // Everything an EPUB reader parses as XML must actually be XML.
        foreach (var (name, text) in files.Where(f => f.Key.EndsWith(".xhtml") || f.Key.EndsWith(".opf") || f.Key.EndsWith(".ncx") || f.Key.EndsWith(".xml")))
            XDocument.Parse(text);                                          // throws with the file name in the failure
    }

    [Fact]
    public void One_file_per_chapter_in_reading_order_with_escaped_text_and_scene_breaks()
    {
        using var t = new TempDir();
        var (book, store) = ExportTests.MakeBook(t);
        var path = Path.Combine(t.Path, "out.epub");

        EpubWriter.Write(ExportSource.Load(book, store), path);
        var files = Read(path);

        Assert.Contains("The Book of One", files["OEBPS/title.xhtml"]);
        Assert.Contains("Ron Eaglin", files["OEBPS/title.xhtml"]);
        Assert.Contains("Part 1 — The Power of One", files["OEBPS/part001.xhtml"]);

        var ch1 = files["OEBPS/chap001.xhtml"];
        Assert.Contains("<h1 class=\"chapter\">Chapter 1: The Message</h1>", ch1);
        Assert.Contains("White text on a black screen &amp; nothing else.", ch1);
        Assert.Contains("<p class=\"sb\">* * *</p>", ch1);
        Assert.Contains("<p class=\"first\">The message appeared.</p>", ch1);   // opens the chapter, so not indented
        Assert.Contains("Second chapter &lt;text&gt;.", files["OEBPS/chap002.xhtml"]);
        Assert.Contains("Chapter 1: Epilogue", files["OEBPS/chap003.xhtml"]);

        // The spine is the reading order: title, the part, its two chapters, then the loose one.
        var opf = files["OEBPS/package.opf"];
        var spine = new[] { "title", "part001", "chap001", "chap002", "chap003" }
            .Select(id => opf.IndexOf($"<itemref idref=\"{id}\"/>", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, spine);
        Assert.Equal(spine.OrderBy(i => i), spine);
        Assert.Contains($"urn:uuid:{book.Id}", opf);
        Assert.Contains("<dc:creator>Ron Eaglin</dc:creator>", opf);
    }

    [Fact]
    public void Both_tables_of_contents_nest_the_chapters_under_their_part()
    {
        using var t = new TempDir();
        var (book, store) = ExportTests.MakeBook(t);
        var path = Path.Combine(t.Path, "out.epub");

        EpubWriter.Write(ExportSource.Load(book, store), path);
        var files = Read(path);

        var nav = XDocument.Parse(files["OEBPS/nav.xhtml"]);
        XNamespace h = "http://www.w3.org/1999/xhtml";
        var top = nav.Descendants(h + "ol").First().Elements(h + "li").ToList();
        Assert.Equal(2, top.Count);                                          // the part, and the chapter outside it
        Assert.Equal("Part 1 — The Power of One", top[0].Element(h + "a")!.Value);
        var nested = top[0].Element(h + "ol")!.Elements(h + "li").Select(li => li.Element(h + "a")!.Value).ToList();
        Assert.Equal(new[] { "Chapter 1: The Message", "Chapter 2: The Mystery" }, nested);
        Assert.Equal("Chapter 1: Epilogue", top[1].Element(h + "a")!.Value);

        var ncx = XDocument.Parse(files["OEBPS/toc.ncx"]);
        XNamespace n = "http://www.daisy.org/z3986/2005/ncx/";
        var points = ncx.Descendants(n + "navMap").First().Elements(n + "navPoint").ToList();
        Assert.Equal(2, points.Count);
        Assert.Equal(2, points[0].Elements(n + "navPoint").Count());         // the part's two chapters nested inside it
    }
}

public class PdfTests
{
    /// <summary>The same book as the other export tests, with one chapter long enough to wrap and justify.</summary>
    private static (Book book, BookStore store) MakeLongBook(TempDir t)
    {
        var (book, store) = ExportTests.MakeBook(t);
        var prose = string.Join("\n\n", Enumerable.Range(1, 40).Select(i =>
            $"Paragraph {i}. The message had been on every screen in the city for eleven minutes, and nobody " +
            "had yet found the sentence that explained it, which was how Roland came to be standing in the " +
            "cold outside the tower with a telephone he did not want to answer ringing in his coat."));
        var ch = new Chapter { Title = "The Long Night", SectionId = book.Sections[0].Id };
        book.Chapters.Insert(2, ch);
        store.SaveChapterBody(book, ch, TextReader_.Read(prose).ToFlowDocumentXaml(), 1200);
        store.Save(book);
        return (book, store);
    }

    private static string[] PageTexts(string path)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        return doc.GetPages().Select(p => p.Text).ToArray();
    }

    [Fact]
    public void Book_is_typeset_in_reading_order_with_a_title_page_part_pages_and_chapter_openings()
    {
        using var t = new TempDir();
        var (book, store) = ExportTests.MakeBook(t);
        var path = Path.Combine(t.Path, "out.pdf");

        PdfBookWriter.Write(ExportSource.Load(book, store), path);

        var pages = PageTexts(path);
        Assert.Equal(5, pages.Length);                                       // title, part, two chapters, the epilogue
        Assert.Contains("The Book of One", pages[0]);
        Assert.Contains("by Ron Eaglin", pages[0]);
        Assert.Contains("Part 1", pages[1]);
        Assert.Contains("CHAPTER 1", pages[2]);
        Assert.Contains("The Message", pages[2]);
        Assert.Contains("White text on a black screen & nothing else.", pages[2]);
        Assert.Contains("* * *", pages[2]);
        Assert.Contains("The Mystery", pages[3]);
        Assert.Contains("Epilogue", pages[4]);
        Assert.DoesNotContain("1", pages[0]);                                // no page number on the title page
    }

    [Fact]
    public void Chapter_bookmarks_follow_the_tree()
    {
        using var t = new TempDir();
        var (book, store) = ExportTests.MakeBook(t);
        var path = Path.Combine(t.Path, "out.pdf");

        PdfBookWriter.Write(ExportSource.Load(book, store), path);

        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        Assert.True(doc.TryGetBookmarks(out var bookmarks));
        var roots = bookmarks.Roots.Select(r => r.Title).ToList();
        Assert.Equal(new[] { "Part 1 — The Power of One", "Chapter 1: Epilogue" }, roots);
        var under = bookmarks.Roots[0].Children.Select(c => c.Title).ToList();
        Assert.Equal(new[] { "Chapter 1: The Message", "Chapter 2: The Mystery" }, under);
    }

    [Fact]
    public void Long_prose_wraps_over_pages_justified_inside_the_margins()
    {
        using var t = new TempDir();
        var (book, store) = MakeLongBook(t);
        var path = Path.Combine(t.Path, "long.pdf");
        var options = new PdfBookOptions();

        PdfBookWriter.Write(ExportSource.Load(book, store), path, options);

        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        var pages = doc.GetPages().ToList();
        Assert.True(pages.Count > 8, $"a 40-paragraph chapter should run to several pages, got {pages.Count}");

        // Words are spaced by position, not by space characters, unless the writer puts them in —
        // so this is what keeps the text copyable out of the PDF across a line break.
        var all = string.Concat(pages.Select(p => p.Text));
        var phrase = "had yet found the sentence that explained it";
        var intact = (all.Length - all.Replace(phrase, "").Length) / phrase.Length;
        Assert.True(intact >= 30, $"the phrase survived line wrapping in only {intact} of 40 paragraphs");

        double width = options.PageWidthIn * 72, margin = options.MarginIn * 72;
        Assert.Equal(width, pages[0].Width, 1);
        Assert.Equal(options.PageHeightIn * 72, pages[0].Height, 1);

        foreach (var letter in pages.SelectMany(p => p.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)))
        {
            Assert.True(letter.GlyphRectangle.Left >= margin - 1, $"'{letter.Value}' sits left of the margin");
            Assert.True(letter.GlyphRectangle.Right <= width - margin + 1, $"'{letter.Value}' runs past the right margin");
        }

        // Justified: on a full page most lines end flush right. Group letters into lines by baseline.
        var body = pages[4];
        var lines = body.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).GroupBy(l => Math.Round(l.StartBaseLine.Y, 0)).ToList();
        var flush = lines.Count(g => g.Max(l => l.GlyphRectangle.Right) >= width - margin - 2);
        Assert.True(flush >= lines.Count / 2, $"only {flush} of {lines.Count} lines reach the right margin");
    }

    [Fact]
    public void A_page_too_small_for_a_line_still_finishes_instead_of_looping()
    {
        using var t = new TempDir();
        var (book, store) = MakeLongBook(t);
        var path = Path.Combine(t.Path, "tiny.pdf");

        // Deliberately absurd: the text block has room for well under one line of 30 pt leading.
        PdfBookWriter.Write(ExportSource.Load(book, store), path,
            new PdfBookOptions { PageWidthIn = 2, PageHeightIn = 1.6, MarginIn = 0.7, BodyPt = 8, LinePt = 30 });

        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        Assert.True(doc.NumberOfPages > 100);                                // one line a page, but it finishes
    }

    [Fact]
    public void Letter_setup_gives_letter_sized_pages()
    {
        using var t = new TempDir();
        var (book, store) = ExportTests.MakeBook(t);
        var path = Path.Combine(t.Path, "letter.pdf");

        PdfBookWriter.Write(ExportSource.Load(book, store), path, PdfBookOptions.Letter);

        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        var page = doc.GetPage(1);
        Assert.Equal(612, page.Width, 1);
        Assert.Equal(792, page.Height, 1);
    }
}
