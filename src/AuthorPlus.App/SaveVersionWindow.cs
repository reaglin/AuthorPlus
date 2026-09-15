using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Services.Versioning;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// "Save a version": what has changed since the last one, and a line saying what this version is.
/// The line is written for the author — <see cref="ChangeNames.Suggest"/> reads the changes and
/// offers "Chapter 12 — The Interrogation revised" — so that pressing Enter is always reasonable
/// and a thought of their own is never required.
/// </summary>
public sealed class SaveVersionWindow : Window
{
    private readonly TextBox _message;

    /// <summary>What the author called this version. Read after the dialog returns true.</summary>
    public string Message => _message.Text.Trim();

    public SaveVersionWindow(IReadOnlyList<BookChange> changes, bool alsoBackUp)
    {
        Title = "Save a Version";
        Width = 560; SizeToContent = SizeToContent.Height; MaxHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = "A version is the whole book as it stands now. You can come back to it at any time.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(new TextBlock { Text = "What is this version?", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _message = new TextBox { Text = ChangeNames.Suggest(changes), Padding = new Thickness(6), FontSize = 14 };
        _message.SelectAll();
        root.Children.Add(_message);

        root.Children.Add(new TextBlock
        {
            Text = changes.Count == 1 ? "Changed since the last version — 1 file:" : $"Changed since the last version — {changes.Count} files:",
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4)
        });

        var list = new ListBox
        {
            MaxHeight = 260, BorderBrush = Palette.Line, Background = Brushes.White,
            ItemsSource = changes.Select(c => $"{c.KindWord}   {c.Description}").ToList()
        };
        root.Children.Add(list);

        if (alsoBackUp)
            root.Children.Add(new TextBlock
            {
                Text = "This book is connected to GitHub — the version is sent there as soon as it is saved.",
                Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
            });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Save version", Width = 120, IsDefault = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", Width = 80, IsCancel = true });
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => { _message.Focus(); _message.SelectAll(); };
    }
}
