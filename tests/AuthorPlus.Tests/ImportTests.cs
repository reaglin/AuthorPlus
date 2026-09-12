using System.IO.Compression;
using System.Text;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Import;

namespace AuthorPlus.Tests;

/// <summary>Builds a minimal but valid .docx in memory: the parts Word requires plus our paragraphs.</summary>
internal static class DocxFixture
{
    public static string Paragraph(string text, string? style = null, bool bold = false, bool italic = false) =>
        "<w:p>" + (style is null ? "" : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>") +
        Run(text, bold, italic) + "</w:p>";

    public static string Run(string text, bool bold = false, bool italic = false, bool underline = false) =>
        "<w:r>" + (bold || italic || underline ? "<w:rPr>" + (bold ? "<w:b/>" : "") + (italic ? "<w:i/>" : "") + (underline ? "<w:u w:val=\"single\"/>" : "") + "</w:rPr>" : "") +
        $"<w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(text)}</w:t></w:r>";

    public static void Write(string path, params string[] paragraphsXml)
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = string.Concat(paragraphsXml);
        var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"{w}\"><w:body>{body}<w:sectPr/></w:body></w:document>";
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Add(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add(zip, "word/document.xml", document);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name);
        using var s = e.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }
}

public class DocxReaderTests
{
    [Fact]
    public void Reads_headings_paragraphs_and_run_formatting_and_renders_flow_document_xaml()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "Chapter 1 - Test.docx");
        DocxFixture.Write(path,
            DocxFixture.Paragraph("Chapter 1 - Test", style: "Heading1"),
            "<w:p>" + DocxFixture.Run("The message ") + DocxFixture.Run("appeared", italic: true) + DocxFixture.Run(" on every screen & \"stayed\".") + "</w:p>",
            DocxFixture.Paragraph(""),
            DocxFixture.Paragraph("Two have been removed.", bold: true),
            DocxFixture.Paragraph(""),
            DocxFixture.Paragraph(""));

        var doc = DocxReader.Read(path);

        Assert.Equal(4, doc.Paragraphs.Count);                       // trailing empties trimmed, inner one kept
        Assert.Equal(1, doc.Paragraphs[0].HeadingLevel);
        Assert.Equal("Chapter 1 - Test", doc.FirstHeading);
        Assert.Equal("The message appeared on every screen & \"stayed\".", doc.Paragraphs[1].Text);
        Assert.True(doc.Paragraphs[1].Runs[1].Italic);
        Assert.False(doc.Paragraphs[1].Runs[0].Italic);
        Assert.True(doc.Paragraphs[3].Runs[0].Bold);
        Assert.Equal(12, doc.WithoutLeadingHeading().WordCount);

        var xaml = doc.WithoutLeadingHeading().ToFlowDocumentXaml();
        Assert.StartsWith("<FlowDocument", xaml);
        Assert.Contains("FontStyle=\"Italic\"", xaml);
        Assert.Contains("FontWeight=\"Bold\"", xaml);
        Assert.Contains("&amp; \"stayed\"", xaml);
        Assert.DoesNotContain("Chapter 1 - Test", xaml);
        Assert.Contains("xml:space=\"preserve\"", xaml);
    }

    [Fact]
    public void Plain_text_and_markdown_split_on_blank_lines_and_read_hash_headings()
    {
        var doc = TextReader_.Read("# Chapter 23 - The Missing One\r\n\r\nFirst line\r\nstill first paragraph.\r\n\r\n\r\nSecond *paragraph*.\r\n");
        Assert.Equal(3, doc.Paragraphs.Count);
        Assert.Equal(1, doc.Paragraphs[0].HeadingLevel);
        Assert.Equal("Chapter 23 - The Missing One", doc.FirstHeading);
        Assert.Equal("First line still first paragraph.", doc.Paragraphs[1].Text);
        Assert.Equal("Second *paragraph*.", doc.Paragraphs[2].Text);
    }
}

public class ChapterFileNameTests
{
    [Theory]
    [InlineData("Chapter One - The Message.docx", 1, "The Message")]
    [InlineData("Chapter Twenty-Eight - The Final Article.docx", 28, "The Final Article")]
    [InlineData("Chapter Twenty-Two - The Decision.docx", 22, "The Decision")]
    [InlineData("Chapter Three - TechScope - What is Behind the Message.docx", 3, "TechScope - What is Behind the Message")]
    [InlineData("Chapter Twelve - The Detective's Revelation.docx", 12, "The Detective's Revelation")]
    [InlineData("Chapter 1 - The intelligence.docx", 1, "The intelligence")]
    [InlineData("Chapter 19 - Eplilogue.docx", 19, "Eplilogue")]
    [InlineData("Chapter 14- The Game.docx", 14, "The Game")]
    [InlineData("Chapter 4- The Project.docx", 4, "The Project")]
    [InlineData("Chapter 6 - Interlude 1 - George.docx", 6, "Interlude 1 - George")]
    [InlineData("Chapter 13 - Edmund's Victory.docx", 13, "Edmund's Victory")]
    [InlineData("Chapter 1 - The Revealing of AGI.docx", 1, "The Revealing of AGI")]
    [InlineData("Chapter 24 - Aesop's Fables.docx", 24, "Aesop's Fables")]
    [InlineData("Chapter One - The First Time it Happened - Google Docs.pdf", 1, "The First Time it Happened")]
    [InlineData("Ch 3: Landfall.txt", 3, "Landfall")]
    [InlineData("12 The Game.md", 12, "The Game")]
    [InlineData("chapter nine – Dmitry.docx", 9, "Dmitry")]
    public void Parses_the_real_file_name_shapes(string file, int number, string title)
    {
        var p = ChapterFileName.TryParse(file);
        Assert.NotNull(p);
        Assert.Equal(number, p!.Number);
        Assert.Equal(title, p.Title);
    }

    [Theory]
    [InlineData("The Book of One - Part One.docx")]
    [InlineData("Evaluation - ChaptGPT (2).docx")]
    [InlineData("Corruption of Two - Draft 1.docx")]
    [InlineData("Cover.jpg")]
    [InlineData("Character Focus Chapter 1-3.docx")]
    [InlineData("The Soul of Three - Draft One.docx")]
    public void Rejects_files_that_are_not_chapters(string file) =>
        Assert.Null(ChapterFileName.TryParse(file));

    [Theory]
    [InlineData("twenty-eight", 28)]
    [InlineData("Twenty Seven", 27)]
    [InlineData("nine", 9)]
    [InlineData("one hundred", 100)]
    public void Number_words(string text, int expected)
    {
        Assert.True(ChapterFileName.TryParseWords(text, out var v));
        Assert.Equal(expected, v);
    }
}

public class ManuscriptImporterTests
{
    private static void Chapter(string folder, string fileName, string heading, string body) =>
        DocxFixture.Write(Path.Combine(folder, fileName), DocxFixture.Paragraph(heading, style: "Heading1"), DocxFixture.Paragraph(body));

    [Fact]
    public void Scan_finds_chapter_files_in_number_order_reports_gaps_duplicates_mismatches_and_skips_the_rest()
    {
        using var t = new TempDir();
        var src = Path.Combine(t.Path, "Part 3");
        Directory.CreateDirectory(Path.Combine(src, "Draft 1"));
        Chapter(src, "Chapter 2 - The Chip.docx", "Chapter 2 - The Chip", "two words here");
        Chapter(src, "Chapter 1 - The Revealing of AGI.docx", "Chapter 1 - The Revealing of AGI", "one two three four");
        Chapter(src, "Chapter 4 - One.docx", "Chapter 4 - Someone Else", "x");
        Chapter(src, "Chapter 4 - One (copy).docx", "Chapter 4 - One", "x");
        Chapter(src, "Chapter 24 - Aesop's Fables.docx", "Chapter 24 - Aesop's Fables", "fable");
        Chapter(Path.Combine(src, "Draft 1"), "Chapter 9 - Old.docx", "Chapter 9", "old draft");
        File.WriteAllText(Path.Combine(src, "The Soul of Three - Draft One.docx"), "not really a docx");
        File.WriteAllText(Path.Combine(src, "notes.txt"), "loose notes");
        File.WriteAllText(Path.Combine(src, "Chapter 23 - Restored.txt"), "# Chapter 23 - Restored\n\nOn another device.\n");

        var plan = new ManuscriptImporter(new BookStore(t.Path)).Scan(src);

        Assert.Equal(new[] { 1, 2, 4, 4, 23, 24 }, plan.Chapters.Select(c => c.Number));
        Assert.Equal("The Revealing of AGI", plan.Chapters[0].Title);
        Assert.Equal(4, plan.Chapters[0].WordCount);
        Assert.Equal(3, plan.Chapters[4].WordCount);                              // txt chapter, heading dropped
        Assert.True(plan.Chapters[2].TitleMismatch || plan.Chapters[3].TitleMismatch);
        Assert.Equal(new[] { 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22 }, plan.MissingNumbers);
        Assert.Equal(new[] { 4 }, plan.DuplicateNumbers);
        Assert.Equal(new[] { "notes.txt", "The Soul of Three - Draft One.docx" }, plan.Skipped);   // alphabetical, case-insensitive
        Assert.All(plan.Chapters, c => Assert.Null(c.Error));
    }

    [Fact]
    public void Import_creates_chapters_in_a_section_with_bodies_and_word_counts_and_leaves_sources_alone()
    {
        using var t = new TempDir();
        var src = Path.Combine(t.Path, "src");
        Directory.CreateDirectory(src);
        Chapter(src, "Chapter Two - Second.docx", "Chapter Two - Second", "second chapter text");
        Chapter(src, "Chapter One - First.docx", "Chapter One - First", "first chapter text here");
        var before = Directory.GetFiles(src).Select(f => (f, File.GetLastWriteTimeUtc(f))).ToList();

        var store = new BookStore(t.Path);
        var book = store.Create("Imported");
        var part = new Section { Title = "Part 1" };
        book.Sections.Add(part);
        var importer = new ManuscriptImporter(store);
        var plan = importer.Scan(src);

        var created = importer.Import(book, plan, part);

        Assert.Equal(new[] { "First", "Second" }, created.Select(c => c.Title));
        Assert.All(created, c => Assert.Equal(part.Id, c.SectionId));
        Assert.Equal(ChapterStatus.Draft, created[0].Status);
        Assert.Equal(4, created[0].WordCount);
        var body = store.LoadChapterBody(book, created[0]);
        Assert.Contains("first chapter text here", body);
        Assert.DoesNotContain("Chapter One - First", body);

        var loaded = store.Load(book.FolderPath);
        Assert.Equal(2, loaded.ChaptersOf(loaded.Sections[0]).Count());
        Assert.Equal(before, Directory.GetFiles(src).Select(f => (f, File.GetLastWriteTimeUtc(f))).ToList());
    }

    [Fact]
    public void ImportOne_inserts_a_chapter_at_a_position_and_defaults_to_the_end_of_its_section()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("Insert");
        var part1 = new Section { Title = "P1" };
        var part2 = new Section { Title = "P2" };
        book.Sections.AddRange(new[] { part1, part2 });
        book.Chapters.Add(new Chapter { Title = "22", SectionId = part1.Id });
        book.Chapters.Add(new Chapter { Title = "24", SectionId = part1.Id });
        book.Chapters.Add(new Chapter { Title = "P2-1", SectionId = part2.Id });

        var file = Path.Combine(t.Path, "Chapter 23 - Found.md");
        File.WriteAllText(file, "# Chapter 23 - Found\n\nWritten on the phone.\n");
        var importer = new ManuscriptImporter(store);

        var ch23 = importer.ImportOne(book, file, null, part1, insertAt: 1);
        Assert.Equal("Found", ch23.Title);
        Assert.Equal(new[] { "22", "Found", "24" }, book.ChaptersOf(part1).Select(c => c.Title));
        Assert.Equal(2, book.ChapterNumber(ch23));
        Assert.Equal(4, ch23.WordCount);

        var tail = importer.ImportOne(book, file, "Tail", part1, insertAt: null);
        Assert.Equal(new[] { "22", "Found", "24", "Tail" }, book.ChaptersOf(part1).Select(c => c.Title));
        Assert.Equal("P2-1", book.Chapters[^1].Title);

        store.Save(book);
        Assert.Contains("Written on the phone.", store.LoadChapterBody(book, ch23));
    }
}
