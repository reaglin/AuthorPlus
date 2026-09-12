using System.IO.Compression;
using System.Xml;

namespace AuthorPlus.Core.Services.Import;

/// <summary>
/// Reads the text of a Word document without any Office library: a .docx is a zip whose
/// <c>word/document.xml</c> holds paragraphs (<c>w:p</c>), each with a style (<c>w:pStyle</c>)
/// and runs (<c>w:r</c>) carrying bold / italic / underline and text (<c>w:t</c>).
/// That is all a manuscript needs; tables, images, footnotes and comments are ignored.
/// </summary>
public static class DocxReader
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static ImportedDocument Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("Not a Word document (no word/document.xml inside).");
        using var stream = entry.Open();
        return Read(stream);
    }

    public static ImportedDocument Read(Stream documentXml)
    {
        var doc = new XmlDocument();
        doc.Load(documentXml);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("w", W);

        var result = new ImportedDocument();
        var body = doc.SelectSingleNode("/w:document/w:body", ns);
        if (body is null) return result;

        foreach (XmlNode p in body.SelectNodes("w:p", ns)!)
        {
            var para = new ImportedParagraph { HeadingLevel = HeadingLevelOf(p.SelectSingleNode("w:pPr/w:pStyle/@w:val", ns)?.Value) };

            foreach (XmlNode r in p.SelectNodes(".//w:r", ns)!)
            {
                var rPr = r.SelectSingleNode("w:rPr", ns);
                bool bold      = IsOn(rPr?.SelectSingleNode("w:b", ns));
                bool italic    = IsOn(rPr?.SelectSingleNode("w:i", ns));
                bool underline = rPr?.SelectSingleNode("w:u", ns) is { } u && u.Attributes?["w:val"]?.Value != "none";

                var text = new System.Text.StringBuilder();
                foreach (XmlNode child in r.ChildNodes)
                {
                    switch (child.LocalName)
                    {
                        case "t":   text.Append(child.InnerText); break;
                        case "tab": text.Append('\t'); break;
                        case "br":  text.Append('\n'); break;
                    }
                }
                if (text.Length > 0) para.Runs.Add(new ImportedRun(text.ToString(), bold, italic, underline));
            }
            result.Paragraphs.Add(para);
        }

        // Trim trailing empty paragraphs Word leaves at the end.
        while (result.Paragraphs.Count > 0 && result.Paragraphs[^1].IsEmpty)
            result.Paragraphs.RemoveAt(result.Paragraphs.Count - 1);
        return result;
    }

    /// <summary>A formatting toggle is "on" when present without w:val, or with w:val true/on/1.</summary>
    private static bool IsOn(XmlNode? node)
    {
        if (node is null) return false;
        var val = node.Attributes?["w:val"]?.Value;
        return val is null || val is "true" or "on" or "1";
    }

    private static int HeadingLevelOf(string? style)
    {
        if (string.IsNullOrEmpty(style)) return 0;
        if (style.Equals("Title", StringComparison.OrdinalIgnoreCase)) return 1;
        if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(style["Heading".Length..], out var n) && n is >= 1 and <= 6) return n;
        return 0;
    }
}

/// <summary>
/// Plain text and Markdown manuscripts: blank lines separate paragraphs, single line breaks
/// inside a paragraph are joined, <c>#</c> lines are headings. Nothing else is interpreted —
/// an asterisk in prose stays an asterisk.
/// </summary>
public static class TextReader_
{
    public static ImportedDocument Read(string text)
    {
        var result = new ImportedDocument();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var buffer = new List<string>();

        void Flush()
        {
            if (buffer.Count == 0) return;
            var p = new ImportedParagraph();
            p.Runs.Add(new ImportedRun(string.Join(' ', buffer.Select(l => l.Trim()))));
            result.Paragraphs.Add(p);
            buffer.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0) { Flush(); continue; }
            if (line.StartsWith('#'))
            {
                Flush();
                int level = line.TakeWhile(c => c == '#').Count();
                var p = new ImportedParagraph { HeadingLevel = Math.Clamp(level, 1, 6) };
                p.Runs.Add(new ImportedRun(line.TrimStart('#').Trim()));
                result.Paragraphs.Add(p);
                continue;
            }
            buffer.Add(line);
        }
        Flush();
        return result;
    }

    public static ImportedDocument ReadFile(string path) => Read(File.ReadAllText(path));
}
