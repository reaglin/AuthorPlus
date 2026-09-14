using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace AuthorPlus.App;

/// <summary>
/// The colours that carry meaning, in one place so the same thing is the same colour everywhere:
/// a plotline's kind reads the same on the board, in a chapter's plotline table and on its page.
/// Every background here is a pale tint meant to be read through — the text on it stays near-black,
/// and nothing relies on colour alone: each tint sits behind a word that says the same thing.
/// </summary>
public static class Palette
{
    private static SolidColorBrush Rgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Table rules and the boards' cell grid.</summary>
    public static readonly Brush Line = Rgb(0xD8, 0xDE, 0xE6);
    /// <summary>Header rows and other quiet furniture.</summary>
    public static readonly Brush HeaderBg = Rgb(0xF1, 0xF4, 0xF8);
    /// <summary>Every row of a table, alternating with white.</summary>
    public static readonly Brush RowAltBg = Rgb(0xFA, 0xFB, 0xFD);
    /// <summary>Text that is a link to another page.</summary>
    public static readonly Brush Link = Rgb(0x1A, 0x5F, 0xB4);

    // What a thread is doing in the chapter being read.
    public static readonly Brush IntroducedBg = Rgb(0xD7, 0xF0, 0xDC);   // it starts here
    public static readonly Brush ContinuedBg  = Rgb(0xE3, 0xEC, 0xFA);   // it was already running
    public static readonly Brush UnlinkedBg   = Rgb(0xFB, 0xEF, 0xD2);   // named, but not linked yet

    // Point of view.
    public static readonly Brush PovBg = Rgb(0xFA, 0xE8, 0xC8);

    // Something the AI wrote, on a character or a plotline, that the author has kept.
    public static readonly Brush AiBg    = Rgb(0xDD, 0xE6, 0xF6);
    public static readonly Brush AiRowBg = Rgb(0xF3, 0xF7, 0xFD);

    /// <summary>What a thread does in one chapter — the same three colours on the board and in the tables.</summary>
    public static Brush RoleBg(PlotlineRole role) => role switch
    {
        PlotlineRole.Introduced => IntroducedBg,
        PlotlineRole.Resolved   => Rgb(0xE6, 0xDD, 0xF3),
        _                       => ContinuedBg
    };

    /// <summary>The dot for a thread's part in a chapter: green it starts, blue it runs on, purple it ends.</summary>
    public static Brush RoleInk(PlotlineRole role) => role switch
    {
        PlotlineRole.Introduced => Rgb(0x1B, 0x84, 0x4B),
        PlotlineRole.Resolved   => Rgb(0x6B, 0x45, 0xA8),
        _                       => Rgb(0x1A, 0x5F, 0xB4)
    };

    /// <summary>The word for a thread's part in a chapter.</summary>
    public static string RoleWord(PlotlineRole role) => role switch
    {
        PlotlineRole.Introduced => "Introduced",
        PlotlineRole.Resolved   => "Resolved",
        _                       => "Continuing"
    };

    /// <summary>The tint behind a plotline kind — strongest for the threads that carry the most.</summary>
    public static Brush KindBg(PlotlineKind kind) => kind switch
    {
        PlotlineKind.Primary   => Rgb(0xCB, 0xDE, 0xF7),
        PlotlineKind.Secondary => Rgb(0xD6, 0xEE, 0xE6),
        PlotlineKind.Chapter   => Rgb(0xFA, 0xE4, 0xC9),
        PlotlineKind.Subplot   => Rgb(0xE6, 0xDD, 0xF3),
        _                      => Rgb(0xE7, 0xEA, 0xEE)
    };

    /// <summary>The dot on the boards, matching the kind's tint but dark enough to see at 12px.</summary>
    public static Brush KindInk(PlotlineKind kind) => kind switch
    {
        PlotlineKind.Primary   => Rgb(0x1A, 0x5F, 0xB4),
        PlotlineKind.Secondary => Rgb(0x1B, 0x84, 0x6B),
        PlotlineKind.Chapter   => Rgb(0xB3, 0x6A, 0x0C),
        PlotlineKind.Subplot   => Rgb(0x6B, 0x45, 0xA8),
        _                      => Rgb(0x69, 0x70, 0x7A)
    };

    /// <summary>A word on its own tint: the kind, "Introduced here", "POV". Colour never carries the meaning alone.</summary>
    public static Border Chip(string text, Brush background, string? tooltip = null) => new()
    {
        Background = background,
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(7, 1, 7, 2),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        ToolTip = tooltip,
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = Rgb(0x1A, 0x1D, 0x21) }
    };

    /// <summary>Text that opens another page when clicked.</summary>
    public static TextBlock LinkText(string text, Action open, string tooltip)
    {
        var block = new TextBlock
        {
            Text = text, Foreground = Link, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        };
        block.MouseLeftButtonUp += (_, _) => open();
        block.MouseEnter += (_, _) => block.TextDecorations = System.Windows.TextDecorations.Underline;
        block.MouseLeave += (_, _) => block.TextDecorations = null;
        return block;
    }

    /// <summary>A table header cell.</summary>
    public static Border HeaderCell(string text) => new()
    {
        Background = HeaderBg, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(8, 5, 8, 5),
        Child = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }
    };

    /// <summary>A table body cell holding any content.</summary>
    public static Border Cell(UIElement content, bool alternate, Brush? background = null) => new()
    {
        Background = background ?? (alternate ? RowAltBg : Brushes.White),
        BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(8, 5, 8, 5),
        Child = content
    };

    /// <summary>A table body cell holding plain text.</summary>
    public static Border Cell(string text, bool alternate, Brush? background = null) =>
        Cell(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, alternate, background);
}
