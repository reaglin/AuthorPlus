using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Services.Export;

namespace AuthorPlus.App;

/// <summary>
/// How the PDF should be set before it is written: the page it is printed on, whether the right
/// margin is straight or ragged, and whether the pages are numbered. Everything else about the
/// export (reading order, parts, chapter openings) follows the book.
/// </summary>
public sealed class ExportPdfWindow : Window
{
    private readonly RadioButton _letter, _a4;
    private readonly CheckBox _justify, _numbers;

    /// <summary>The setting the author chose. Read after the dialog returns true.</summary>
    public PdfBookOptions Options =>
        (_letter.IsChecked == true ? PdfBookOptions.Letter : _a4.IsChecked == true ? PdfBookOptions.A4 : new PdfBookOptions())
        with { Ragged = _justify.IsChecked != true, PageNumbers = _numbers.IsChecked == true };

    public ExportPdfWindow(string bookTitle, int chapters, int words)
    {
        Title = "Export to PDF";
        Width = 470; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = $"“{bookTitle}” — {chapters} chapters, {words:N0} words. Every chapter opens on a new page, " +
                   "each part gets a page of its own, and the contents become PDF bookmarks.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });

        root.Children.Add(Label("Page"));
        Choice(root, "Book — 6 × 9 in", "The size most paperbacks are printed at.", true);
        _letter = Choice(root, "Letter — 8½ × 11 in", "Ordinary US paper, set double-spaced for reading and marking up.", false);
        _a4     = Choice(root, "A4", "The same setting on A4 paper.", false);

        root.Children.Add(Label("Setting"));
        _justify = Tick(root, "Straight right margin", "Spaces are widened so every line ends at the margin, as in a printed book. Off leaves the right edge ragged.", true);
        _numbers = Tick(root, "Number the pages", "A page number at the foot of every page except the title and part pages.", true);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = "Export…", Width = 100, IsDefault = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", Width = 80, IsCancel = true });
        root.Children.Add(buttons);

        Content = root;
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) };

    private static RadioButton Choice(Panel parent, string text, string note, bool chosen)
    {
        var button = new RadioButton { IsChecked = chosen, Margin = new Thickness(4, 3, 0, 3), Content = Caption(text, note) };
        parent.Children.Add(button);
        return button;
    }

    private static CheckBox Tick(Panel parent, string text, string note, bool ticked)
    {
        var box = new CheckBox { IsChecked = ticked, Margin = new Thickness(4, 3, 0, 3), Content = Caption(text, note) };
        parent.Children.Add(box);
        return box;
    }

    private static TextBlock Caption(string text, string note)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
        block.Inlines.Add(new System.Windows.Documents.Run(text));
        block.Inlines.Add(new System.Windows.Documents.LineBreak());
        block.Inlines.Add(new System.Windows.Documents.Run(note) { Foreground = System.Windows.Media.Brushes.Gray, FontSize = 12 });
        return block;
    }
}
