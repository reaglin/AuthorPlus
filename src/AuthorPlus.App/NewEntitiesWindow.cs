using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Services;

namespace AuthorPlus.App;

/// <summary>
/// After an analysis, the extraction pass may name characters and plotlines the book does not
/// have yet. This lists them, pre-ticked, and adds the ones the author keeps ticked.
/// </summary>
public sealed class NewEntitiesWindow : Window
{
    private readonly List<(CheckBox Box, ExtractedEntity Entity)> _characters = new();
    private readonly List<(CheckBox Box, ExtractedEntity Entity)> _plotlines = new();

    public List<ExtractedEntity> ChosenCharacters { get; } = new();
    public List<ExtractedEntity> ChosenPlotlines { get; } = new();

    public NewEntitiesWindow(string chapterTitle, IReadOnlyList<ExtractedEntity> newCharacters, IReadOnlyList<ExtractedEntity> newPlotlines, string? intro = null)
    {
        Title = $"New in \"{chapterTitle}\"";
        Width = 640; Height = 520; MinWidth = 480; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };
        var head = new TextBlock
        {
            Text = intro ?? "The analysis found characters and plotlines that are not in the book yet. Ticked ones are added and linked to this chapter; untick anything that is a passing name rather than a character, or a beat rather than a thread. Known ones were linked already.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Add ticked", Width = 110, IsDefault = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        var skip = new Button { Content = "Add none", Width = 100, IsCancel = true };
        ok.Click += (_, _) =>
        {
            ChosenCharacters.AddRange(_characters.Where(x => x.Box.IsChecked == true).Select(x => x.Entity));
            ChosenPlotlines.AddRange(_plotlines.Where(x => x.Box.IsChecked == true).Select(x => x.Entity));
            DialogResult = true; Close();
        };
        buttons.Children.Add(ok); buttons.Children.Add(skip);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var panel = new StackPanel();
        Section(panel, "New characters", newCharacters, _characters);
        Section(panel, "New plotlines", newPlotlines, _plotlines);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    private static void Section(StackPanel panel, string title, IReadOnlyList<ExtractedEntity> entities, List<(CheckBox, ExtractedEntity)> boxes)
    {
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        if (entities.Count == 0) { panel.Children.Add(new TextBlock { Text = "none", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(12, 0, 0, 0) }); return; }
        foreach (var e in entities)
        {
            var box = new CheckBox { IsChecked = true, Margin = new Thickness(12, 2, 0, 2) };
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
            text.Inlines.Add(new System.Windows.Documents.Run(e.Name) { FontWeight = FontWeights.SemiBold });
            if (e.IsPov) text.Inlines.Add(new System.Windows.Documents.Run("  (POV)") { Foreground = System.Windows.Media.Brushes.Gray });
            if (e.Kind.Length > 0) text.Inlines.Add(new System.Windows.Documents.Run($"  [{e.Kind}]") { Foreground = System.Windows.Media.Brushes.Gray });
            if (e.Chapters.Count > 0) text.Inlines.Add(new System.Windows.Documents.Run($"  chapters {string.Join(", ", e.Chapters)}") { Foreground = System.Windows.Media.Brushes.Gray });
            if (e.Note.Length > 0) text.Inlines.Add(new System.Windows.Documents.Run(" — " + e.Note));
            box.Content = text;
            panel.Children.Add(box);
            boxes.Add((box, e));
        }
    }
}
