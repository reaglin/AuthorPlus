using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Models;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// The chapter, paragraph by paragraph, so the author can pick the passage they want rewrites for
/// without hunting for it in the editor. <b>Select</b> on a paragraph takes that one; ticking
/// several takes them together, in the order they appear, joined as they stand in the chapter.
/// The other way in is the editor itself: select any text there and use "Suggestions for
/// selection…", which is what this window falls back to describing when nothing is picked.
/// </summary>
public sealed class PassagePickerWindow : Window
{
    private readonly List<(CheckBox Box, string Text)> _rows = new();

    /// <summary>The passage the author settled on, or empty when they cancelled.</summary>
    public string Passage { get; private set; } = "";

    public PassagePickerWindow(Chapter chapter, IReadOnlyList<string> paragraphs)
    {
        Title = $"Choose a passage — {chapter.Title}";
        Width = 940; Height = 740; MinWidth = 640; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };

        var head = new TextBlock
        {
            Text = "Pick the passage you want rewrites for. Select takes that paragraph on its own; tick several and use the button below to take them together. " +
                   "You can also select any words in the chapter editor and use Suggestions for Selection — that works on part of a paragraph, or across several.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var useTicked = new Button { Content = "Use the ticked paragraphs", Padding = new Thickness(14, 4, 14, 4), IsDefault = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        useTicked.Click += (_, _) =>
        {
            var picked = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Text).ToList();
            if (picked.Count == 0)
            {
                MessageBox.Show(this, "Tick the paragraphs you want, or use Select on a single one.", "Choose a passage", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Take(string.Join("\n\n", picked));
        };
        buttons.Children.Add(useTicked); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var stack = new StackPanel();
        int n = 0;
        foreach (var text in paragraphs)
        {
            n++;
            var row = new Border
            {
                BorderBrush = new System.Windows.Media.SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE6)), BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 6, 0, 6)
            };
            var dock = new DockPanel();
            row.Child = dock;

            var select = new Button { Content = "Select", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Ask for rewrites of this paragraph" };
            var body = text;
            select.Click += (_, _) => Take(body);
            DockPanel.SetDock(select, Dock.Right);
            dock.Children.Add(select);

            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 8, 0), ToolTip = "Take this paragraph together with the others you tick" };
            DockPanel.SetDock(box, Dock.Left);
            dock.Children.Add(box);
            _rows.Add((box, text));

            var number = new TextBlock { Text = $"{n}.", Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top, MinWidth = 26 };
            DockPanel.SetDock(number, Dock.Left);
            dock.Children.Add(number);

            var prose = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Georgia"), FontSize = 14, MaxHeight = 150 };
            dock.Children.Add(prose);

            stack.Children.Add(row);
        }
        if (n == 0) stack.Children.Add(new TextBlock { Text = "This chapter has no text yet.", Foreground = Brushes.Gray });

        root.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    private void Take(string passage)
    {
        Passage = passage.Trim();
        DialogResult = true;
        Close();
    }

    /// <summary>The chapter's paragraphs: its text split on blank lines and line breaks, blanks dropped.</summary>
    public static IReadOnlyList<string> Paragraphs(string chapterText) =>
        chapterText.Replace("\r\n", "\n").Replace('\r', '\n')
                   .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(p => p.Length > 0)
                   .ToList();
}
