using System.IO;
using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Services.Transfer;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// Import Project: says what is in the file and — when this computer already has the same book — how the two
/// copies relate (<see cref="ImportSituation"/>, from content fingerprints, not save times), then asks whether the
/// file replaces the copy here or comes in beside it. Replacing never deletes: the old copy goes to "Replaced books".
/// When both copies have new writing, or the file is the older one, "keep both" is chosen and the window says why.
/// After an earlier "keep both" there can be several copies here; the author picks which one the file is for.
/// </summary>
public sealed class ImportProjectWindow : Window
{
    private readonly ProjectManifest _manifest;
    private readonly Func<ExistingBook, ImportSituation> _situationOf;
    private readonly string _replacedFolder;
    private readonly IReadOnlyList<ExistingBook> _copies;
    private readonly ComboBox? _which;
    private readonly StackPanel _situationPanel = new();
    private RadioButton? _replace;

    /// <summary>What the author chose. Read after the dialog returns true.</summary>
    public ProjectImportMode Mode => _replace?.IsChecked == true ? ProjectImportMode.Replace : ProjectImportMode.KeepBoth;

    /// <summary>The copy here that the file is for (the one replaced), or null when the book is new here.</summary>
    public ExistingBook? Target => _copies.Count == 0 ? null : _copies[Math.Max(0, _which?.SelectedIndex ?? 0)];

    public ImportProjectWindow(ProjectManifest manifest, IReadOnlyList<ExistingBook> copies, Func<ExistingBook, ImportSituation> situationOf,
                               string fileName, string replacedFolder, string? openFolder)
    {
        _manifest = manifest; _copies = copies; _situationOf = situationOf; _replacedFolder = replacedFolder;
        Title = "Author+ — Import Project";
        Width = 600; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "A project file is your whole book in one file, made on another computer with File › Export Project. " +
                   "Nothing on this computer changes until you press Import.",
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(new TextBlock { Text = $"“{manifest.Title}”", FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(manifest.Author)) facts.Add($"by {manifest.Author}");
        facts.Add($"exported from {(string.IsNullOrWhiteSpace(manifest.ExportedFrom) ? "another computer" : manifest.ExportedFrom)} on {When(manifest.ExportedUtc)}");
        facts.Add(manifest.IncludesHistory ? "with its saved versions" : "no saved versions");
        root.Children.Add(new TextBlock { Text = string.Join(" · ", facts), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        root.Children.Add(new TextBlock { Text = $"File: {fileName}", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (string.Equals(manifest.ExportedFrom, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            root.Children.Add(new TextBlock
            {
                Text = "This file was made on this computer. To bring back writing done elsewhere, choose the file exported on the other computer.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
            });

        if (copies.Count > 1)
        {
            root.Children.Add(new TextBlock
            {
                Text = $"This computer has {copies.Count} copies of this book. Which one is this file for?",
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 4)
            });
            _which = new ComboBox
            {
                ItemsSource = copies.Select(c => $"“{Path.GetFileName(c.Folder)}” — last changed {When(c.ModifiedUtc)}").ToList(),
                ToolTip = "Each is a folder in your books. The file is compared with the one you choose, and replaces it if you say so."
            };
            int open = openFolder == null ? -1 : copies.ToList().FindIndex(c => string.Equals(Path.GetFullPath(c.Folder), Path.GetFullPath(openFolder), StringComparison.OrdinalIgnoreCase));
            _which.SelectedIndex = open >= 0 ? open : 0;
            _which.SelectionChanged += (_, _) => ShowSituation();
            root.Children.Add(_which);
        }

        root.Children.Add(_situationPanel);
        ShowSituation();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Import", Width = 100, IsDefault = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Import the book and open it" };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", Width = 80, IsCancel = true, ToolTip = "Close without changing anything" });
        root.Children.Add(buttons);

        Content = root;
    }

    /// <summary>What the chosen copy here and the file are to each other, and the two choices with the right one ticked.</summary>
    private void ShowSituation()
    {
        _situationPanel.Children.Clear();
        _replace = null;
        var here = Target;
        if (here is null)
        {
            _situationPanel.Children.Add(Para("This book is not on this computer yet. It will be added to your books and opened."));
            return;
        }

        var situation = _situationOf(here);
        var folder = Path.GetFileName(here.Folder);
        _situationPanel.Children.Add(new TextBlock
        {
            Text = $"This computer already has this book, in the folder “{folder}”, last changed {When(here.ModifiedUtc)}.",
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 6)
        });

        var (message, warn, replaceFirst) = situation switch
        {
            ImportSituation.FileIsNewer => ("The file has new writing, and the copy here has not changed since it last went out or came in. Replacing it is the usual choice.", false, true),
            ImportSituation.Same => ("The file holds exactly what is already here — there is nothing new in it. You can press Cancel.", false, true),
            ImportSituation.FileIsOlder =>
                ("⚠ The copy here is newer than this file: you have written here since it was made, and the file has nothing new. " +
                 "If you meant to bring back writing from the other computer, export it there again and import that file. If you don't need this file, press Cancel.", true, false),
            ImportSituation.BothChanged =>
                ("⚠ Both computers have new writing since the book was last exported or imported here. Keep both, then compare them " +
                 "(Versions › Version History in each) and copy across what you need. Replacing would set this computer's new writing aside.", true, false),
            ImportSituation.ProbablyOlder =>
                ($"⚠ Going by the dates, the copy here is newer (changed {When(here.ModifiedUtc)}; the file's copy was changed {When(_manifest.BookModifiedUtc)}). " +
                 "Keeping both is safest; if you don't need this file, press Cancel.", true, false),
            _ => ($"Going by the dates, the file is newer (its copy was changed {When(_manifest.BookModifiedUtc)}). Replacing the copy here is the usual choice.", false, true),
        };
        if (warn)
            _situationPanel.Children.Add(new Border { Background = Palette.UnlinkedBg, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), Child = Para(message, 0) });
        else
            _situationPanel.Children.Add(Para(message, 0, 0, 8));

        _replace = new RadioButton
        {
            GroupName = "mode", IsChecked = replaceFirst, Margin = new Thickness(0, 4, 0, 0),
            Content = Para($"Replace “{folder}” with the file's copy. The copy here is moved to “{_replacedFolder}”, not deleted.", 0),
            ToolTip = "The imported book takes the place of the one here, under the same folder name."
        };
        var keep = new RadioButton
        {
            GroupName = "mode", IsChecked = !replaceFirst, Margin = new Thickness(0, 8, 0, 0),
            Content = Para("Keep both — add the file's copy as a separate book, with a number after its folder name.", 0),
            ToolTip = "Nothing here is moved; you can compare the two and delete one later."
        };
        _situationPanel.Children.Add(_replace);
        _situationPanel.Children.Add(keep);
    }

    private static TextBlock Para(string text, double top = 14, double left = 0, double bottom = 0) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(left, top, 0, bottom) };

    private static string When(DateTime utc) =>
        utc == default ? "(unknown)" : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy, h:mm tt");
}
