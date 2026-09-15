using System.Text;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services.Import;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace AuthorPlus.Core.Services.Export;

/// <summary>How the printed book is set. Defaults are a 6×9 in trade paperback.</summary>
public sealed record PdfBookOptions
{
    public double PageWidthIn  { get; init; } = 6;
    public double PageHeightIn { get; init; } = 9;
    public double MarginIn     { get; init; } = 0.75;
    /// <summary>Body size in points. Headings are sized from it.</summary>
    public double BodyPt       { get; init; } = 11;
    /// <summary>Baseline to baseline, in points.</summary>
    public double LinePt       { get; init; } = 16;
    /// <summary>First-line indent of a paragraph, in inches. The paragraph that opens a block is not indented.</summary>
    public double IndentIn     { get; init; } = 0.25;
    /// <summary>Print a page number at the foot of every page of the manuscript.</summary>
    public bool   PageNumbers  { get; init; } = true;
    /// <summary>Set the right margin ragged instead of justifying the text.</summary>
    public bool   Ragged       { get; init; }

    /// <summary>US Letter, for an author who wants to print the manuscript on ordinary paper.</summary>
    public static PdfBookOptions Letter => new() { PageWidthIn = 8.5, PageHeightIn = 11, MarginIn = 1, BodyPt = 12, LinePt = 24 };

    /// <summary>A4, the same setting as <see cref="Letter"/> on the paper the rest of the world uses.</summary>
    public static PdfBookOptions A4 => new() { PageWidthIn = 8.27, PageHeightIn = 11.69, MarginIn = 1, BodyPt = 12, LinePt = 24 };
}

/// <summary>
/// The book as a finished PDF: a title page, a page for each part, every chapter opening on a
/// new page, justified prose with a first-line indent, a page number at the foot and PDF
/// bookmarks that mirror the tree.
///
/// PDFsharp (MIT) writes the file; the page is laid out here — measuring each word, filling
/// lines, and spacing them out to the margin — because a novel needs the typesetting, not a
/// generic report. Fonts come from <see cref="BookFontResolver"/>, so Core keeps no UI
/// dependency.
/// </summary>
public static class PdfBookWriter
{
    public static void Write(ExportSource src, string path, PdfBookOptions? options = null)
    {
        BookFontResolver.Install();
        if (BookFontResolver.AvailableFamily is null)
            throw new InvalidOperationException(
                "No serif font (Georgia, Constantia, Times New Roman…) was found on this computer, " +
                "so the PDF cannot be typeset. Export to Word or EPUB instead.");

        using var w = new Typesetter(src, options ?? new PdfBookOptions());
        w.Run();
        w.Save(path);
    }

    // ── The typesetter ────────────────────────────────────────────────────────

    private sealed class Typesetter : IDisposable
    {
        private readonly ExportSource _src;
        private readonly PdfBookOptions _o;
        private readonly PdfDocument _doc = new();

        private readonly double _pageW, _pageH, _left, _right, _top, _bottom, _textW, _indent;
        private readonly XFont[] _body;          // indexed by Style (regular, bold, italic, bold-italic)
        private readonly XFont _bookTitle, _bookAuthor, _partTitle, _chapterNo, _chapterTitle, _subhead, _folio;
        private readonly XBrush _ink = XBrushes.Black, _quiet = new XSolidBrush(XColor.FromArgb(110, 110, 110));

        private readonly Dictionary<(string, int), double> _widths = new();
        private readonly double[] _spaceWidth = new double[4];
        private bool _measured;

        private PdfPage? _page;
        private XGraphics? _gfx;
        private double _y;
        private int _folioNumber;

        public Typesetter(ExportSource src, PdfBookOptions o)
        {
            _src = src;
            _o = o;
            _pageW = o.PageWidthIn * 72;
            _pageH = o.PageHeightIn * 72;
            _left = o.MarginIn * 72;
            _right = _pageW - _left;
            _top = o.MarginIn * 72;
            _bottom = _pageH - o.MarginIn * 72;
            _textW = _right - _left;
            _indent = o.IndentIn * 72;

            var f = BookFontResolver.BookSerif;
            _body =
            [
                new XFont(f, o.BodyPt, XFontStyleEx.Regular),
                new XFont(f, o.BodyPt, XFontStyleEx.Bold),
                new XFont(f, o.BodyPt, XFontStyleEx.Italic),
                new XFont(f, o.BodyPt, XFontStyleEx.BoldItalic),
            ];
            _bookTitle    = new XFont(f, o.BodyPt + 15, XFontStyleEx.Bold);
            _bookAuthor   = new XFont(f, o.BodyPt + 3,  XFontStyleEx.Italic);
            _partTitle    = new XFont(f, o.BodyPt + 10, XFontStyleEx.Bold);
            _chapterNo    = new XFont(f, o.BodyPt - 1,  XFontStyleEx.Regular);
            _chapterTitle = new XFont(f, o.BodyPt + 5,  XFontStyleEx.Bold);
            _subhead      = new XFont(f, o.BodyPt + 1,  XFontStyleEx.Bold);
            _folio        = new XFont(f, o.BodyPt - 2,  XFontStyleEx.Regular);

            var b = _src.Book;
            _doc.Info.Title = b.Title;
            _doc.Info.Author = b.Author;
            _doc.Info.Subject = b.Genre;
            _doc.Info.Creator = "Author+";
        }

        public void Run()
        {
            TitlePage();
            MeasureSpaces();

            PdfOutline? partBookmark = null;
            Section? lastSection = null;
            foreach (var (section, ch, n) in _src.Walk())
            {
                if (_src.SectionHeadings && section is not null && !ReferenceEquals(section, lastSection))
                {
                    PartPage(section);
                    partBookmark = _doc.Outlines.Add(section.Title, _page!, false);
                    lastSection = section;
                }
                ChapterPage(ch, n, partBookmark, section is null || !_src.SectionHeadings);
            }
        }

        public void Save(string path) => _doc.Save(path);

        public void Dispose()
        {
            _gfx?.Dispose();
            _doc.Dispose();
        }

        // ── Pages ─────────────────────────────────────────────────────────────

        private void NewPage(bool folio)
        {
            _gfx?.Dispose();
            _page = _doc.AddPage();
            _page.Width = XUnit.FromPoint(_pageW);
            _page.Height = XUnit.FromPoint(_pageH);
            _gfx = XGraphics.FromPdfPage(_page);
            _y = _top;
            if (folio && _o.PageNumbers)
            {
                _folioNumber++;
                Centred(_folioNumber.ToString(), _folio, _pageH - _o.MarginIn * 36, _quiet);
            }
        }

        private void TitlePage()
        {
            var b = _src.Book;
            NewPage(folio: false);
            _y = _top + (_bottom - _top) * 0.30;
            _y = CentredBlock(b.Title, _bookTitle, _y);
            if (b.Author.Length > 0)
            {
                _y += _bookTitle.GetHeight() * 0.6;
                _y = CentredBlock("by " + b.Author, _bookAuthor, _y);
            }
        }

        private void PartPage(Section section)
        {
            NewPage(folio: false);
            _y = _top + (_bottom - _top) * 0.33;
            _y = CentredBlock(section.Title, _partTitle, _y);
        }

        private void ChapterPage(Chapter ch, int number, PdfOutline? part, bool topLevelBookmark)
        {
            NewPage(folio: true);
            _y = _top + (_bottom - _top) * 0.12;                    // the drop every chapter opens with

            if (_src.NumberChapters)
            {
                Centred($"CHAPTER {number}", _chapterNo, _y, _quiet);
                _y += _chapterNo.GetHeight() * 1.8;
            }
            _y = CentredBlock(ch.Title, _chapterTitle, _y);
            _y += _chapterTitle.GetHeight() * 1.05;

            var label = (_src.NumberChapters ? $"Chapter {number}: " : "") + ch.Title;
            if (topLevelBookmark || part is null) _doc.Outlines.Add(label, _page!, false);
            else part.Outlines.Add(label, _page!, false);

            bool firstOfBlock = true;
            foreach (var p in _src.Bodies.TryGetValue(ch.Id, out var d) ? d.Paragraphs : new List<ImportedParagraph>())
            {
                if (p.IsEmpty) continue;
                if (FlowDocumentXaml.IsSceneBreak(p))
                {
                    Room(_o.LinePt * 3);
                    _y += _o.LinePt * 0.7;
                    Centred(FlowDocumentXaml.SceneBreak, _body[0], _y);
                    _y += _o.LinePt * 1.7;
                    firstOfBlock = true;
                    continue;
                }
                if (p.HeadingLevel > 0)
                {
                    Room(_o.LinePt * 4);                            // never a subhead alone at the foot
                    _y += _o.LinePt * 0.8;
                    _gfx!.DrawString(p.Text.Trim(), _subhead, _ink, _left, _y, XStringFormats.TopLeft);
                    _y += _subhead.GetHeight() * 1.5;
                    firstOfBlock = true;
                    continue;
                }
                Paragraph(p, firstOfBlock);
                firstOfBlock = false;
            }
        }

        // ── Paragraph setting ─────────────────────────────────────────────────

        private readonly record struct Piece(string Text, XFont Font, double Width);

        /// <summary>A word, its pieces in the styles the runs gave it; <c>Break</c> is a forced line end.</summary>
        private sealed class Word
        {
            public List<Piece> Pieces { get; } = new();
            public double Width;
            public bool Break;
            public int LastStyle;
        }

        private void Paragraph(ImportedParagraph p, bool firstOfBlock)
        {
            var words = WordsOf(p);
            if (words.Count == 0) return;

            // Fill lines, then set them — knowing the whole paragraph is what lets a page break
            // avoid leaving one line stranded at the foot of a page or carrying one to the next.
            var lines = new List<(List<Word> Words, double Natural, bool LastOfParagraph)>();
            var line = new List<Word>();
            double natural = 0;
            double firstIndent = firstOfBlock ? 0 : _indent;

            foreach (var word in words)
            {
                if (word.Break)
                {
                    lines.Add((line, natural, true));
                    line = new List<Word>();
                    natural = 0;
                    continue;
                }
                double avail = _textW - (lines.Count == 0 ? firstIndent : 0);
                double space = line.Count == 0 ? 0 : _spaceWidth[line[^1].LastStyle];
                if (line.Count > 0 && natural + space + word.Width > avail)
                {
                    lines.Add((line, natural, false));
                    line = new List<Word>();
                    natural = 0;
                    space = 0;
                }
                natural += space + word.Width;
                line.Add(word);
            }
            if (line.Count > 0) lines.Add((line, natural, true));
            if (lines.Count == 0) return;

            int start = 0;
            while (start < lines.Count)
            {
                int remaining = lines.Count - start;
                int room = (int)Math.Floor((_bottom - _y) / _o.LinePt);

                // On a fresh page whatever is left is set regardless: turning the page again would
                // gain nothing, and a page setup too small for one line must not loop forever.
                bool freshPage = _y <= _top + 0.01;
                if (room < 1)
                {
                    if (!freshPage) { NewPage(folio: true); continue; }
                    room = 1;
                }

                int take = Math.Min(room, remaining);
                if (take < remaining)
                {
                    // An orphan is the paragraph's first line alone at the foot of a page; a widow
                    // is its last line alone at the top of the next. Both are moved, never split.
                    if (start == 0 && take == 1 && !freshPage) { NewPage(folio: true); continue; }
                    if (remaining - take == 1 && take >= 2) take--;
                }

                for (int i = 0; i < take; i++)
                {
                    var (ws, nat, last) = lines[start + i];
                    double indent = (start + i) == 0 ? firstIndent : 0;
                    double extra = last || _o.Ragged || ws.Count < 2 ? 0 : _textW - indent - nat;
                    Line(ws, _left + indent, extra);
                    _y += _o.LinePt;
                }
                start += take;
                if (start < lines.Count) NewPage(folio: true);
            }
        }

        /// <summary>Paragraph text as words, each carrying the styled pieces it is made of.</summary>
        private List<Word> WordsOf(ImportedParagraph p)
        {
            var words = new List<Word>();
            Word? current = null;
            var buffer = new StringBuilder();
            int style = 0;

            void FlushPiece()
            {
                if (buffer.Length == 0) return;
                current ??= New();
                var text = buffer.ToString();
                var piece = new Piece(text, _body[style], Width(text, style));
                current.Pieces.Add(piece);
                current.Width += piece.Width;
                current.LastStyle = style;
                buffer.Clear();
            }
            Word New() { var w = new Word(); words.Add(w); return w; }

            foreach (var r in p.Runs)
            {
                int s = (r.Bold ? 1 : 0) | (r.Italic ? 2 : 0);
                foreach (var chr in r.Text)
                {
                    if (chr == '\n')
                    {
                        FlushPiece();
                        current = null;
                        words.Add(new Word { Break = true });
                        continue;
                    }
                    if (char.IsWhiteSpace(chr))
                    {
                        FlushPiece();
                        current = null;
                        continue;
                    }
                    if (s != style) { FlushPiece(); style = s; }
                    buffer.Append(chr);
                }
                FlushPiece();
                style = s;
            }
            FlushPiece();

            while (words.Count > 0 && words[^1].Break) words.RemoveAt(words.Count - 1);
            return words.Where(w => w.Break || w.Pieces.Count > 0).ToList();
        }

        /// <summary>
        /// Draws one line of a paragraph: the words left to right from <paramref name="x"/>,
        /// with <paramref name="extra"/> shared out between the spaces so the last word lands
        /// on the right margin. A word is drawn piece by piece, each in its own face.
        /// </summary>
        private void Line(List<Word> words, double x, double extra)
        {
            double share = words.Count > 1 ? extra / (words.Count - 1) : 0;
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) x += _spaceWidth[words[i - 1].LastStyle] + share;
                var pieces = words[i].Pieces;
                for (int j = 0; j < pieces.Count; j++)
                {
                    // Every word carries its own trailing space, including the one that ends the
                    // line, so text copied out of the PDF has the spaces the manuscript has. The
                    // space glyph has no ink and x is advanced by the word alone, so where the
                    // next word starts — and so the justification — is unaffected.
                    var space = j == pieces.Count - 1;
                    _gfx!.DrawString(space ? pieces[j].Text + " " : pieces[j].Text, pieces[j].Font, _ink, x, _y, XStringFormats.TopLeft);
                    x += pieces[j].Width;
                }
            }
        }

        // ── Small helpers ─────────────────────────────────────────────────────

        private void Centred(string text, XFont font, double y, XBrush? brush = null)
        {
            var w = _gfx!.MeasureString(text, font).Width;
            _gfx.DrawString(text, font, brush ?? _ink, (_pageW - w) / 2, y, XStringFormats.TopLeft);
        }

        /// <summary>
        /// A heading centred over as many lines as it needs — book, part and chapter titles are
        /// the author's words and can be long. Returns the y the next thing may start at.
        /// </summary>
        private double CentredBlock(string text, XFont font, double y, XBrush? brush = null)
        {
            var height = font.GetHeight() * 1.15;
            foreach (var line in WrapToWidth(text, font))
            {
                Centred(line, font, y, brush);
                y += height;
            }
            return y;
        }

        private List<string> WrapToWidth(string text, XFont font)
        {
            var lines = new List<string>();
            var line = new StringBuilder();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var trial = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && _gfx!.MeasureString(trial, font).Width > _textW)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                }
                else line.Clear().Append(trial);
            }
            if (line.Length > 0) lines.Add(line.ToString());
            if (lines.Count == 0) lines.Add(text);                  // a title of nothing but spaces
            return lines;
        }

        /// <summary>Starts a new page unless <paramref name="needed"/> points still fit.</summary>
        private void Room(double needed)
        {
            if (_y + needed > _bottom) NewPage(folio: true);
        }

        /// <summary>
        /// A word's width in one style. Prose repeats itself heavily, so the cache turns a
        /// 160,000-word book from a MeasureString per word into a dictionary hit per word.
        /// </summary>
        private double Width(string text, int style)
        {
            if (_widths.TryGetValue((text, style), out var w)) return w;
            w = _gfx!.MeasureString(text, _body[style]).Width;
            _widths[(text, style)] = w;
            return w;
        }

        /// <summary>
        /// The width of a space, per style. MeasureString trims, so it is taken as the
        /// difference two letters make with and without a space between them.
        /// </summary>
        private void MeasureSpaces()
        {
            if (_measured) return;
            for (int s = 0; s < 4; s++)
                _spaceWidth[s] = _gfx!.MeasureString("n n", _body[s]).Width - _gfx.MeasureString("nn", _body[s]).Width;
            _measured = true;
        }
    }
}
