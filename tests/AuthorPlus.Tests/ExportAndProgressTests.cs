using System.IO.Compression;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Export;
using AuthorPlus.Core.Services.Import;

namespace AuthorPlus.Tests;

public class FlowDocumentXamlTests
{
    private const string Ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void Parses_what_XamlWriter_produces_including_nested_formatting_and_headings()
    {
        // The shape WPF's XamlWriter.Save gives a RichTextBox document (attributes on Run, nested Bold/Italic, Spans).
        var xaml =
            $"<FlowDocument xmlns=\"{Ns}\" FontFamily=\"Georgia\" FontSize=\"15\">" +
            "<Paragraph FontWeight=\"Bold\" FontSize=\"22\"><Run>Chapter Heading</Run></Paragraph>" +
            "<Paragraph><Run>The message </Run><Run FontStyle=\"Italic\">appeared</Run><Run> on every screen.</Run></Paragraph>" +
            "<Paragraph><Bold><Run>Two</Run> have</Bold> been <Italic><Underline><Run>removed</Run></Underline></Italic>.<LineBreak/><Run>Next line</Run></Paragraph>" +
            "<Paragraph TextAlignment=\"Center\"><Run>* * *</Run></Paragraph>" +
            "<Paragraph><Span FontWeight=\"SemiBold\"><Run Text=\"Attr text\"/></Span></Paragraph>" +
            "<Paragraph/>" +
            "</FlowDocument>";

        var doc = FlowDocumentXaml.Parse(xaml);

        Assert.Equal(6, doc.Paragraphs.Count);
        Assert.Equal(1, doc.Paragraphs[0].HeadingLevel);
        Assert.Equal("Chapter Heading", doc.Paragraphs[0].Text);
        Assert.Equal("The message appeared on every screen.", doc.Paragraphs[1].Text);
        Assert.True(doc.Paragraphs[1].Runs[1].Italic);
        Assert.False(doc.Paragraphs[1].Runs[0].Italic);

        var p2 = doc.Paragraphs[2];
        Assert.Equal("Two have been removed.\nNext line", p2.Text);
        Assert.True(p2.Runs[0].Bold);          // "Two"
        Assert.True(p2.Runs[1].Bold);          // " have"
        Assert.False(p2.Runs[2].Bold);         // " been "
        var removed = p2.Runs.Single(r => r.Text == "removed");
        Assert.True(removed.Italic && removed.Underline);

        Assert.True(FlowDocumentXaml.IsSceneBreak(doc.Paragraphs[3]));
        Assert.True(doc.Paragraphs[4].Runs[0].Bold);
        Assert.Equal("Attr text", doc.Paragraphs[4].Text);
        Assert.True(doc.Paragraphs[5].IsEmpty);
    }

    [Fact]
    public void Round_trips_through_the_importers_xaml()
    {
        var original = TextReader_.Read("# Title\n\nFirst paragraph.\n\nSecond, with *stars* kept literal.");
        var back = FlowDocumentXaml.Parse(original.ToFlowDocumentXaml());
        Assert.Equal(original.PlainText, back.PlainText);
        Assert.Equal(1, back.Paragraphs[0].HeadingLevel);
    }
}

public class ExportTests
{
    private static (Book book, BookStore store) MakeBook(TempDir t)
    {
        var store = new BookStore(t.Path);
        var book = store.Create("The Book of One", "Ron Eaglin");
        var part1 = new Section { Title = "Part 1 — The Power of One" };
        book.Sections.Add(part1);
        var c1 = new Chapter { Title = "The Message", SectionId = part1.Id };
        var c2 = new Chapter { Title = "The Mystery", SectionId = part1.Id };
        var loose = new Chapter { Title = "Epilogue" };
        book.Chapters.AddRange(new[] { c1, c2, loose });
        store.SaveChapterBody(book, c1, TextReader_.Read("The message appeared.\n\nWhite text on a black screen & nothing else.\n\n* * *\n\nLater.").ToFlowDocumentXaml(), 12);
        store.SaveChapterBody(book, c2, TextReader_.Read("Second chapter <text>.").ToFlowDocumentXaml(), 3);
        store.Save(book);
        return (book, store);
    }

    [Fact]
    public void Markdown_has_title_section_and_chapter_headings_in_reading_order_with_scene_breaks()
    {
        using var t = new TempDir();
        var (book, store) = MakeBook(t);
        var md = MarkdownExporter.Render(ExportSource.Load(book, store));

        Assert.StartsWith("# The Book of One\n*by Ron Eaglin*\n", md);
        var iPart = md.IndexOf("# Part 1 — The Power of One", StringComparison.Ordinal);
        var iCh1 = md.IndexOf("## Chapter 1: The Message", StringComparison.Ordinal);
        var iCh2 = md.IndexOf("## Chapter 2: The Mystery", StringComparison.Ordinal);
        var iEpi = md.IndexOf("## Chapter 1: Epilogue", StringComparison.Ordinal);
        Assert.True(0 < iPart && iPart < iCh1 && iCh1 < iCh2 && iCh2 < iEpi);
        Assert.Contains("White text on a black screen & nothing else.\n\n* * *\n\nLater.", md);
        Assert.Contains("Second chapter <text>.", md);
    }

    [Fact]
    public void Docx_is_a_valid_package_that_our_own_reader_reads_back()
    {
        using var t = new TempDir();
        var (book, store) = MakeBook(t);
        var path = Path.Combine(t.Path, "out.docx");

        DocxWriter.Write(ExportSource.Load(book, store), path);

        using (var zip = ZipFile.OpenRead(path))
        {
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
            Assert.NotNull(zip.GetEntry("word/document.xml"));
            Assert.NotNull(zip.GetEntry("word/styles.xml"));
            Assert.NotNull(zip.GetEntry("docProps/core.xml"));
        }

        var back = DocxReader.Read(path);
        var texts = back.Paragraphs.Where(p => !p.IsEmpty).Select(p => p.Text).ToList();
        Assert.Equal("The Book of One", texts[0]);
        Assert.Equal("by Ron Eaglin", texts[1]);
        Assert.Contains("Part 1 — The Power of One", texts);
        Assert.Contains("Chapter 1: The Message", texts);
        Assert.Contains("White text on a black screen & nothing else.", texts);
        Assert.Contains("* * *", texts);
        Assert.Contains("Second chapter <text>.", texts);
        Assert.Contains("Chapter 1: Epilogue", texts);
        Assert.Equal(1, back.Paragraphs.First(p => p.Text == "Part 1 — The Power of One").HeadingLevel);
        Assert.Equal(2, back.Paragraphs.First(p => p.Text == "Chapter 1: The Message").HeadingLevel);
        Assert.True(texts.IndexOf("Chapter 1: The Message") < texts.IndexOf("Chapter 2: The Mystery"));
    }
}

public class ProgressTests
{
    [Fact]
    public void Records_first_count_of_the_day_and_latest_and_reports_words_added()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("P");
        var day = new DateTime(2026, 9, 12, 9, 0, 0);

        var p = Progress.Load(book);
        p.Touch(1000, day);
        p.Touch(1250, day.AddHours(3));
        p.Touch(1200, day.AddHours(5));      // deleted some
        p.Save(book);

        var back = Progress.Load(book);
        Assert.Equal(200, back.WordsToday(day));
        Assert.Equal(0, back.WordsToday(day.AddDays(1)));
        back.Touch(1200, day.AddDays(1));
        back.Touch(1300, day.AddDays(1));
        Assert.Equal(100, back.WordsToday(day.AddDays(1)));
        Assert.Equal(200, back.WordsToday(day));
        Assert.True(File.Exists(Progress.PathFor(book)));
    }
}
