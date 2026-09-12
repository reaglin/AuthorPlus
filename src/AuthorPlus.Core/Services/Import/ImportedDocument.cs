using System.Text;
using System.Xml;

namespace AuthorPlus.Core.Services.Import;

/// <summary>A run of text with the three formats a manuscript actually uses.</summary>
public sealed record ImportedRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false);

/// <summary>One paragraph. <see cref="HeadingLevel"/> 0 = body text, 1–6 = Heading1–6 / Title.</summary>
public sealed class ImportedParagraph
{
    public int HeadingLevel { get; init; }
    public List<ImportedRun> Runs { get; } = new();
    public string Text => string.Concat(Runs.Select(r => r.Text));
    public bool IsEmpty => Text.Trim().Length == 0;
}

/// <summary>
/// The format-neutral result of reading a manuscript file (DOCX, TXT, Markdown). It knows how
/// to render itself as the FlowDocument XAML the chapter editor stores.
/// </summary>
public sealed class ImportedDocument
{
    public List<ImportedParagraph> Paragraphs { get; } = new();

    /// <summary>The first heading's text, if the document starts with one ("Chapter One - The Message").</summary>
    public string? FirstHeading => Paragraphs.FirstOrDefault(p => p.HeadingLevel > 0 && !p.IsEmpty)?.Text.Trim();

    /// <summary>Body paragraphs joined by blank lines; headings included on their own lines.</summary>
    public string PlainText
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var p in Paragraphs)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(p.Text.Trim());
            }
            return sb.ToString();
        }
    }

    public int WordCount => WordCounter.Count(PlainText);

    /// <summary>A copy without the leading heading paragraph(s) — used when the chapter title comes from the file name.</summary>
    public ImportedDocument WithoutLeadingHeading()
    {
        var copy = new ImportedDocument();
        bool skipping = true;
        foreach (var p in Paragraphs)
        {
            if (skipping && (p.HeadingLevel > 0 || p.IsEmpty)) continue;
            skipping = false;
            copy.Paragraphs.Add(p);
        }
        return copy;
    }

    /// <summary>
    /// FlowDocument XAML in the shape WPF's XamlReader parses and the chapter editor saves:
    /// one Paragraph per paragraph, Runs with FontWeight/FontStyle/TextDecorations, headings
    /// as bold, larger paragraphs. Empty paragraphs are kept as spacing.
    /// </summary>
    public string ToFlowDocumentXaml()
    {
        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true, Encoding = new UTF8Encoding(false) };
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, settings))
        {
            const string ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            w.WriteStartElement("FlowDocument", ns);
            w.WriteAttributeString("FontFamily", "Georgia");
            w.WriteAttributeString("FontSize", "15");
            w.WriteAttributeString("LineHeight", "24");

            foreach (var p in Paragraphs)
            {
                w.WriteStartElement("Paragraph", ns);
                if (p.HeadingLevel > 0)
                {
                    w.WriteAttributeString("FontWeight", "Bold");
                    w.WriteAttributeString("FontSize", p.HeadingLevel == 1 ? "22" : "18");
                    w.WriteAttributeString("Margin", "0,18,0,8");
                }
                foreach (var r in p.Runs)
                {
                    if (r.Text.Length == 0) continue;
                    var lines = r.Text.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i > 0) w.WriteStartElement("LineBreak", ns) ; if (i > 0) w.WriteEndElement();
                        if (lines[i].Length == 0) continue;
                        w.WriteStartElement("Run", ns);
                        if (r.Bold)      w.WriteAttributeString("FontWeight", "Bold");
                        if (r.Italic)    w.WriteAttributeString("FontStyle", "Italic");
                        if (r.Underline) w.WriteAttributeString("TextDecorations", "Underline");
                        w.WriteAttributeString("xml", "space", null, "preserve");
                        w.WriteString(lines[i]);
                        w.WriteEndElement();
                    }
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        return sb.ToString();
    }
}
