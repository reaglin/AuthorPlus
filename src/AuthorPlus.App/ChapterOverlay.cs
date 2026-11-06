using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AuthorPlus.App;

/// <summary>A paragraph of the open chapter as Analysis mode shows it: its ¶ icon colour and whether it is the one open in the panel.</summary>
public sealed record ParagraphMark(Paragraph Paragraph, int Index, Brush Fill, string Tip);

/// <summary>A name in the open chapter as Character mode shows it: a character's (coloured) or a possible new one (dotted underline).</summary>
public sealed record NameMark(TextPointer Start, TextPointer End, Brush? Fill, object Target, string Tip);

/// <summary>A place new text can go in Create mode: before paragraph <see cref="Index"/>, or after the last when it equals the count.</summary>
public sealed record InsertPoint(int Index);

/// <summary>
/// Drawn over the chapter's RichTextBox, never into its document — so nothing it shows is saved
/// with the chapter. In Analysis mode: a coloured ¶ in the left margin at the start of each
/// paragraph, and a bar beside the paragraph open in the panel. In Character mode: each
/// character's name washed in their colour, and possible new names underlined with dots.
/// Only the icons and names take the mouse; clicks anywhere else go to the text.
/// </summary>
public sealed class ChapterOverlay : Adorner
{
    private readonly RichTextBox _rtb;
    private IReadOnlyList<ParagraphMark> _paragraphs = Array.Empty<ParagraphMark>();
    private IReadOnlyList<NameMark> _names = Array.Empty<NameMark>();
    private IReadOnlyList<Paragraph> _inserts = Array.Empty<Paragraph>();
    private int _insertAt = -1;
    private int _newStart = -1;
    private int _insertCount;
    private int _selected = -1;
    private object? _selectedTarget;
    private NameMark? _currentMark;
    private TextPointer? _speakStart, _speakEnd;
    private Paragraph? _startPara;
    private static readonly Brush Speaking = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xD5, 0x4F));
    private readonly List<(Rect Rect, object Target, string Tip)> _hits = new();

    /// <summary>A ¶ icon was clicked: the paragraph's index.</summary>
    public event Action<int>? ParagraphClicked;
    /// <summary>A name was clicked: its <see cref="NameMark.Target"/>.</summary>
    public event Action<object>? NameClicked;
    /// <summary>A + was clicked: where the new text goes (see <see cref="InsertPoint"/>).</summary>
    public event Action<int>? InsertClicked;

    public const double IconSize = 18;
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x1A, 0x5F, 0xB4));
    private static readonly Brush Band = new SolidColorBrush(Color.FromArgb(0x16, 0x1A, 0x5F, 0xB4));
    private static readonly Pen Dots = new(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 1.4) { DashStyle = new DashStyle(new double[] { 1, 2 }, 0) };
    private static readonly Pen Ring = new(Brushes.Black, 1.5);

    public ChapterOverlay(RichTextBox rtb) : base(rtb)
    {
        _rtb = rtb;
        IsHitTestVisible = true;
        Cursor = Cursors.Hand;
        rtb.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => InvalidateVisual()));
        rtb.SizeChanged += (_, _) => InvalidateVisual();
    }

    public void ShowParagraphs(IReadOnlyList<ParagraphMark> marks, int selected)
    {
        _paragraphs = marks; _names = Array.Empty<NameMark>(); _inserts = Array.Empty<Paragraph>(); _insertAt = -1; _selected = selected;
        InvalidateVisual();
    }

    /// <summary>
    /// Create mode: a + before each of the paragraphs and one after the last. <paramref name="selected"/> is the
    /// chosen place (drawn as a line across the text); <paramref name="newCount"/> paragraphs from there are the
    /// text just put in (drawn with a band).
    /// </summary>
    public void ShowInserts(IReadOnlyList<Paragraph> paragraphs, int selected, int newStart = -1, int newCount = 0)
    {
        _inserts = paragraphs; _insertAt = selected; _newStart = newStart; _insertCount = newCount;
        _paragraphs = Array.Empty<ParagraphMark>(); _names = Array.Empty<NameMark>();
        InvalidateVisual();
    }

    public void ShowNames(IReadOnlyList<NameMark> marks, object? selectedTarget)
    {
        _inserts = Array.Empty<Paragraph>(); _insertAt = -1;
        _names = marks; _paragraphs = Array.Empty<ParagraphMark>(); _selectedTarget = selectedTarget; _currentMark = null;
        InvalidateVisual();
    }

    /// <summary>The mention Previous / Next is on: drawn with a strong underline so it stands out from the others.</summary>
    public void SetCurrent(NameMark? mark)
    {
        _currentMark = mark;
        InvalidateVisual();
    }

    /// <summary>Listen mode: where Play starts — a ▶ in the margin beside that paragraph (null: none).</summary>
    public void SetStart(Paragraph? paragraph)
    {
        _startPara = paragraph;
        InvalidateVisual();
    }

    /// <summary>Listen mode: the piece being read aloud, highlighted (null, null: none).</summary>
    public void SetSpeaking(TextPointer? start, TextPointer? end)
    {
        _speakStart = start; _speakEnd = end;
        InvalidateVisual();
    }

    public void Clear()
    {
        _paragraphs = Array.Empty<ParagraphMark>(); _names = Array.Empty<NameMark>(); _inserts = Array.Empty<Paragraph>(); _insertAt = -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        _hits.Clear();
        double height = _rtb.ActualHeight, width = _rtb.ActualWidth;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(0, width - SystemParameters.VerticalScrollBarWidth), height)));

        foreach (var m in _paragraphs)
        {
            Rect top, bottom;
            try
            {
                top = m.Paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                bottom = m.Paragraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            }
            catch (InvalidOperationException) { continue; }       // the paragraph left the document
            if (top.IsEmpty || bottom.Bottom < 0 || top.Top > height) continue;

            if (m.Index == _selected)
            {
                dc.DrawRectangle(Band, null, new Rect(IconSize + 10, top.Top - 2, Math.Max(0, width - IconSize - 30), Math.Max(4, bottom.Bottom - top.Top + 4)));
                dc.DrawRectangle(Accent, null, new Rect(IconSize + 10, top.Top - 2, 3, Math.Max(4, bottom.Bottom - top.Top + 4)));
            }
            var icon = new Rect(5, top.Top + Math.Max(0, (top.Height - IconSize) / 2), IconSize, IconSize);
            dc.DrawRoundedRectangle(m.Fill, m.Index == _selected ? Ring : null, icon, 4, 4);
            var glyph = new FormattedText("¶", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(glyph, new Point(icon.Left + (IconSize - glyph.Width) / 2, icon.Top + (IconSize - glyph.Height) / 2));
            _hits.Add((icon, m.Index, m.Tip));
        }

        foreach (var n in _names)
        {
            foreach (var r in Rects(n.Start, n.End))
            {
                if (r.Bottom < 0 || r.Top > height) continue;
                if (n.Fill != null)
                {
                    dc.DrawRoundedRectangle(n.Fill, ReferenceEquals(n.Target, _selectedTarget) || Equals(n.Target, _selectedTarget) ? Ring : null, r, 3, 3);
                }
                else
                {
                    dc.DrawLine(Dots, new Point(r.Left, r.Bottom - 1), new Point(r.Right, r.Bottom - 1));
                }
                if (ReferenceEquals(n, _currentMark))
                    dc.DrawRectangle(Accent, null, new Rect(r.Left, r.Bottom - 1, r.Width, 3));
                _hits.Add((r, n.Target, n.Tip));
            }
        }
        DrawInserts(dc, width, height);
        if (_startPara != null)
        {
            Rect at = Rect.Empty;
            try { at = _startPara.ContentStart.GetCharacterRect(LogicalDirection.Forward); } catch (InvalidOperationException) { }
            if (!at.IsEmpty && at.Bottom > 0 && at.Top < height)
            {
                double y = at.Top + at.Height / 2;
                var tri = new StreamGeometry();
                using (var g = tri.Open())
                {
                    g.BeginFigure(new Point(8, y - 7), true, true);
                    g.LineTo(new Point(20, y), true, false);
                    g.LineTo(new Point(8, y + 7), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(Accent, null, tri);
            }
        }
        if (_speakStart != null && _speakEnd != null)
            foreach (var r in Rects(_speakStart, _speakEnd))
            {
                if (r.Bottom < 0 || r.Top > height) continue;
                dc.DrawRectangle(Speaking, null, r);
                dc.DrawRectangle(Accent, null, new Rect(r.Left, r.Bottom - 1, r.Width, 2));
            }
        dc.Pop();
    }

    private static readonly Pen InsertLine = new(new SolidColorBrush(Color.FromRgb(0x2E, 0x85, 0x40)), 2) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
    private static readonly Brush InsertFill = new SolidColorBrush(Color.FromRgb(0x2E, 0x85, 0x40));
    private static readonly Brush NewBand = new SolidColorBrush(Color.FromArgb(0x30, 0x2E, 0x85, 0x40));

    /// <summary>The + marks: one in the gap above each paragraph, one under the last.</summary>
    private void DrawInserts(DrawingContext dc, double width, double height)
    {
        if (_inserts.Count == 0 && _insertAt < 0 && _newStart < 0) return;
        var tops = new List<(Rect Top, Rect Bottom)>();
        foreach (var p in _inserts)
        {
            try { tops.Add((p.ContentStart.GetCharacterRect(LogicalDirection.Forward), p.ContentEnd.GetCharacterRect(LogicalDirection.Backward))); }
            catch (InvalidOperationException) { return; }
        }
        // The new text just put in: a green band beside it.
        if (_insertCount > 0 && _newStart >= 0 && _newStart + _insertCount <= tops.Count)
        {
            var a = tops[_newStart].Top; var b = tops[_newStart + _insertCount - 1].Bottom;
            dc.DrawRectangle(InsertFill, null, new Rect(IconSize + 10, a.Top - 2, 3, Math.Max(4, b.Bottom - a.Top + 4)));
            if (!a.IsEmpty && !b.IsEmpty)
                dc.DrawRectangle(NewBand, null, new Rect(IconSize + 10, a.Top - 2, Math.Max(0, width - IconSize - 30), Math.Max(4, b.Bottom - a.Top + 4)));
        }
        for (int i = 0; i <= tops.Count; i++)
        {
            double y;
            if (tops.Count == 0) y = 14;
            else if (i == 0) y = tops[0].Top.Top - 10;
            else if (i == tops.Count) y = tops[^1].Bottom.Bottom + 14;
            else y = (tops[i - 1].Bottom.Bottom + tops[i].Top.Top) / 2;
            if (double.IsNaN(y) || y < -IconSize || y > height + IconSize) continue;
            var icon = new Rect(5, y - IconSize / 2, IconSize, IconSize);
            bool chosen = i == _insertAt;
            if (chosen) dc.DrawLine(InsertLine, new Point(IconSize + 10, y), new Point(width - 24, y));
            dc.DrawEllipse(chosen ? InsertFill : Brushes.White, new Pen(InsertFill, 1.4), new Point(icon.Left + IconSize / 2, y), IconSize / 2, IconSize / 2);
            var glyph = new FormattedText("+", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 15,
                chosen ? Brushes.White : InsertFill, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(glyph, new Point(icon.Left + (IconSize - glyph.Width) / 2, icon.Top + (IconSize - glyph.Height) / 2 - 1));
            _hits.Add((icon, new InsertPoint(i),
                i == 0 ? "Write new text here, before the first paragraph"
                : i == tops.Count ? "Write new text here, at the end of the chapter"
                : "Write new text here, between these two paragraphs"));
        }
    }

    /// <summary>The boxes a stretch of text covers: one per line it runs over.</summary>
    private static IEnumerable<Rect> Rects(TextPointer start, TextPointer end)
    {
        Rect a, b;
        try { a = start.GetCharacterRect(LogicalDirection.Forward); b = end.GetCharacterRect(LogicalDirection.Backward); }
        catch (InvalidOperationException) { yield break; }
        if (a.IsEmpty || b.IsEmpty) yield break;
        if (Math.Abs(a.Top - b.Top) < 1)
        {
            yield return new Rect(a.Left - 1, a.Top, Math.Max(2, b.Left - a.Left + 2), a.Height);
            yield break;
        }
        // The name wraps onto the next line: walk it a character at a time to find where.
        var p = start;
        var lineStart = a;
        var last = a;
        while (p != null && p.CompareTo(end) < 0)
        {
            var next = p.GetNextInsertionPosition(LogicalDirection.Forward);
            if (next == null) break;
            var r = next.GetCharacterRect(LogicalDirection.Backward);
            if (Math.Abs(r.Top - lineStart.Top) >= 1)
            {
                yield return new Rect(lineStart.Left - 1, lineStart.Top, Math.Max(2, last.Right - lineStart.Left + 2), lineStart.Height);
                lineStart = p.GetCharacterRect(LogicalDirection.Forward);
            }
            last = r;
            p = next;
        }
        yield return new Rect(lineStart.Left - 1, lineStart.Top, Math.Max(2, b.Left - lineStart.Left + 2), lineStart.Height);
    }

    private (Rect Rect, object Target, string Tip)? HitAt(Point p)
    {
        foreach (var h in _hits) if (h.Rect.Contains(p)) return h;
        return null;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters p) =>
        HitAt(p.HitPoint) != null ? new PointHitTestResult(this, p.HitPoint) : null;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tip = HitAt(e.GetPosition(this))?.Tip;
        if (!Equals(ToolTip, tip)) ToolTip = tip;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (HitAt(e.GetPosition(this)) != null) e.Handled = true;    // keep the click from moving the caret
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var hit = HitAt(e.GetPosition(this));
        if (hit == null) { base.OnMouseLeftButtonUp(e); return; }
        e.Handled = true;
        if (hit.Value.Target is int index) ParagraphClicked?.Invoke(index);
        else if (hit.Value.Target is InsertPoint at) InsertClicked?.Invoke(at.Index);
        else NameClicked?.Invoke(hit.Value.Target);
    }
}
