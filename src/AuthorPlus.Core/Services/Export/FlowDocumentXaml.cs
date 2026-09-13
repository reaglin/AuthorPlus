using System.Text;
using System.Xml;
using AuthorPlus.Core.Services.Import;

namespace AuthorPlus.Core.Services.Export;

/// <summary>
/// Reads the FlowDocument XAML the chapter editor saves (via WPF's XamlWriter) back into the
/// neutral <see cref="ImportedDocument"/> shape — without WPF, so exports are testable anywhere.
/// Understands Paragraph, Run, Span, Bold, Italic, Underline, Hyperlink, LineBreak, List/ListItem
/// and Section; FontWeight/FontStyle/TextDecorations attributes; a bold paragraph of 18 pt or
/// more counts as a heading. Anything else is walked for its text.
/// </summary>
public static class FlowDocumentXaml
{
    public static ImportedDocument Parse(string xaml)
    {
        var result = new ImportedDocument();
        if (string.IsNullOrWhiteSpace(xaml)) return result;

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xaml);
        Walk(doc.DocumentElement, result, new Inherited(false, false, false), null);
        return result;
    }

    private readonly record struct Inherited(bool Bold, bool Italic, bool Underline)
    {
        public Inherited With(XmlElement e)
        {
            var b = Bold || e.LocalName == "Bold" || Attr(e, "FontWeight") is "Bold" or "SemiBold" or "ExtraBold" or "Black";
            var i = Italic || e.LocalName == "Italic" || Attr(e, "FontStyle") is "Italic" or "Oblique";
            var u = Underline || e.LocalName == "Underline" || (Attr(e, "TextDecorations")?.Contains("Underline", StringComparison.OrdinalIgnoreCase) ?? false);
            return new Inherited(b, i, u);
        }
    }

    private static string? Attr(XmlElement e, string name) => e.HasAttribute(name) ? e.GetAttribute(name) : null;

    private static void Walk(XmlNode? node, ImportedDocument result, Inherited inh, ImportedParagraph? current)
    {
        if (node is null) return;
        foreach (XmlNode child in node.ChildNodes)
        {
            switch (child)
            {
                case XmlElement e when e.LocalName == "Paragraph":
                {
                    var p = new ImportedParagraph { HeadingLevel = HeadingLevelOf(e) };
                    result.Paragraphs.Add(p);
                    Walk(e, result, inh.With(e), p);
                    break;
                }
                case XmlElement e when e.LocalName is "List":
                {
                    Walk(e, result, inh, null);
                    break;
                }
                case XmlElement e when e.LocalName is "ListItem" or "Section" or "FlowDocument" or "BlockUIContainer" or "Table" or "TableRowGroup" or "TableRow" or "TableCell" or "Floater" or "Figure":
                    Walk(e, result, inh, null);
                    break;
                case XmlElement e when e.LocalName == "LineBreak":
                    (current ?? Ensure(result, ref current)).Runs.Add(new ImportedRun("\n"));
                    break;
                case XmlElement e when e.LocalName == "Run":
                {
                    var text = e.HasAttribute("Text") ? e.GetAttribute("Text") : e.InnerText;
                    if (text.Length == 0) break;
                    var f = inh.With(e);
                    (current ?? Ensure(result, ref current)).Runs.Add(new ImportedRun(text, f.Bold, f.Italic, f.Underline));
                    break;
                }
                case XmlElement e:   // Span, Bold, Italic, Underline, Hyperlink, InlineUIContainer…
                    Walk(e, result, inh.With(e), current ?? Ensure(result, ref current));
                    break;
                case XmlWhitespace:
                    break;                                                   // pretty-print indentation, never content
                case XmlText or XmlSignificantWhitespace or XmlCDataSection:
                {
                    var text = child.Value ?? "";
                    if (text.Length == 0) break;
                    (current ?? Ensure(result, ref current)).Runs.Add(new ImportedRun(text, inh.Bold, inh.Italic, inh.Underline));
                    break;
                }
            }
        }
    }

    private static ImportedParagraph Ensure(ImportedDocument result, ref ImportedParagraph? current)
    {
        current = new ImportedParagraph();
        result.Paragraphs.Add(current);
        return current;
    }

    private static int HeadingLevelOf(XmlElement paragraph)
    {
        var weight = Attr(paragraph, "FontWeight");
        var sizeText = Attr(paragraph, "FontSize");
        if (weight is "Bold" or "SemiBold" && double.TryParse(sizeText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size))
            return size >= 21 ? 1 : size >= 17 ? 2 : 0;
        return 0;
    }

    /// <summary>Plain text of the XAML: paragraphs separated by blank lines.</summary>
    public static string ToPlainText(string xaml) => Parse(xaml).PlainText;

    /// <summary>The "scene break" paragraph the editor inserts: three spaced asterisks, centred.</summary>
    public const string SceneBreak = "* * *";

    public static bool IsSceneBreak(ImportedParagraph p)
    {
        var t = p.Text.Trim().Replace(" ", "");
        return t.Length is >= 3 and <= 5 && t.All(c => c is '*' or '#' or '~');
    }

    /// <summary>Convenience for tests and tools: the text as the editor would store it.</summary>
    public static string FromPlainText(string text)
    {
        var doc = TextReader_.Read(text);
        return doc.ToFlowDocumentXaml();
    }

    internal static string Escape(string s) => new StringBuilder(s).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").ToString();
}
