using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Import;
using WinForms = System.Windows.Forms;

namespace AuthorPlus.App;

/// <summary>
/// "Import chapters from a folder": pick the folder, review what was found (numbers, titles,
/// word counts, gaps, duplicates, title mismatches, skipped files), choose the target section
/// and whose titles to use, then import. The source files are never modified.
/// </summary>
public sealed class ImportWindow : Window
{
    private const string BookLevel = "(no section — book level)";
    private const string NewSection = "New section…";

    private readonly Book _book;
    private readonly ManuscriptImporter _importer;
    private readonly TextBox _folderBox = new() { IsReadOnly = true, Padding = new Thickness(4) };
    private readonly ComboBox _sectionBox = new() { MinWidth = 260 };
    private readonly TextBox _newSectionBox = new() { MinWidth = 260, Padding = new Thickness(4), Visibility = Visibility.Collapsed };
    private readonly RadioButton _fileTitles = new() { Content = "file names", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    private readonly RadioButton _docTitles = new() { Content = "headings inside the documents" };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _importButton = new() { Content = "Import", Width = 100, IsEnabled = false, FontWeight = FontWeights.SemiBold };
    private ImportPlan? _plan;

    /// <summary>Chapters created, once the dialog returns true.</summary>
    public IReadOnlyList<Chapter> Created { get; private set; } = Array.Empty<Chapter>();
    /// <summary>The section imported into (possibly newly created), or null for book level.</summary>
    public Section? TargetSection { get; private set; }

    private sealed class Row
    {
        public required ImportEntry Entry { get; init; }
        public bool Include { get => Entry.Include; set => Entry.Include = value; }
        public int Number => Entry.Number;
        public string Title => Entry.Title;
        public string DocumentTitle => Entry.DocumentTitle ?? "";
        public int Words => Entry.WordCount;
        public string Note => Entry.Error ?? (Entry.TitleMismatch ? "title differs inside the file" : "");
    }

    public ImportWindow(Book book, BookStore store, Section? preselect, string? initialFolder = null)
    {
        _book = book;
        _importer = new ManuscriptImporter(store);
        Title = "Import Chapters from a Folder";
        Width = 900; Height = 640; MinWidth = 760; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };

        // Folder row
        var folderRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var browse = new Button { Content = "Choose folder…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => ChooseFolder();
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(new TextBlock { Text = "Folder of chapter files:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        folderRow.Children.Add(_folderBox);
        DockPanel.SetDock(folderRow, Dock.Top);
        root.Children.Add(folderRow);

        var hint = new TextBlock
        {
            Text = "Files named like \"Chapter 12 - The Game.docx\" or \"Chapter Twelve - The Game.docx\" (also .txt / .md) are read; anything else is skipped. Subfolders are ignored. Your files are not changed.",
            Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        // Options row
        var options = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        options.Children.Add(new TextBlock { Text = "Put the chapters in:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var sections = new List<object> { BookLevel };
        sections.AddRange(book.Sections);
        sections.Add(NewSection);
        _sectionBox.ItemsSource = sections;
        _sectionBox.DisplayMemberPath = null;
        _sectionBox.ItemTemplate = SectionTemplate();
        _sectionBox.SelectedItem = preselect is not null ? preselect : (book.Sections.Count == 0 ? NewSection : BookLevel);
        var namedLabel = new TextBlock { Text = " named ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        void ShowNewSection() { var v = ReferenceEquals(_sectionBox.SelectedItem, NewSection) ? Visibility.Visible : Visibility.Collapsed; _newSectionBox.Visibility = v; namedLabel.Visibility = v; }
        _sectionBox.SelectionChanged += (_, _) => ShowNewSection();
        _newSectionBox.Text = "Part " + (book.Sections.Count + 1);
        options.Children.Add(_sectionBox);
        options.Children.Add(namedLabel);
        options.Children.Add(_newSectionBox);
        ShowNewSection();
        options.Children.Add(new TextBlock { Text = "      Titles from:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        options.Children.Add(_fileTitles);
        options.Children.Add(_docTitles);
        _docTitles.Checked += (_, _) => { if (_plan is not null) { _plan.UseDocumentTitles = true; UpdateSummary(); } };
        _fileTitles.Checked += (_, _) => { if (_plan is not null) { _plan.UseDocumentTitles = false; UpdateSummary(); } };
        DockPanel.SetDock(options, Dock.Top);
        root.Children.Add(options);

        // Buttons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        _importButton.Click += (_, _) => DoImport();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_importButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        DockPanel.SetDock(_summary, Dock.Bottom);
        root.Children.Add(_summary);

        // Grid
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Import", Binding = new Binding(nameof(Row.Include)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 60 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(Row.Number)), IsReadOnly = true, Width = 40 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Title (file name)", Binding = new Binding(nameof(Row.Title)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Title inside the document", Binding = new Binding(nameof(Row.DocumentTitle)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Words", Binding = new Binding(nameof(Row.Words)) { StringFormat = "N0" }, IsReadOnly = true, Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Note", Binding = new Binding(nameof(Row.Note)), IsReadOnly = true, Width = 190 });
        _grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(UpdateSummary);
        root.Children.Add(_grid);

        Content = root;
        if (initialFolder is not null && Directory.Exists(initialFolder)) Scan(initialFolder);
    }

    private static DataTemplate SectionTemplate()
    {
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new Binding(".") { Converter = new SectionLabelConverter() });
        return new DataTemplate { VisualTree = f };
    }

    private sealed class SectionLabelConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => value is Section s ? s.Title : value?.ToString() ?? "";
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }

    private void ChooseFolder()
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Choose the folder that holds the chapter files",
            UseDescriptionForTitle = true,
            InitialDirectory = _folderBox.Text.Length > 0 ? _folderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK) Scan(dlg.SelectedPath);
    }

    private void Scan(string folder)
    {
        _folderBox.Text = folder;
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            _plan = _importer.Scan(folder);
            _plan.UseDocumentTitles = _docTitles.IsChecked == true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not read that folder:\n\n{ex.Message}", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            _plan = null;
        }
        finally { Cursor = null; }

        _grid.ItemsSource = _plan?.Chapters.Select(e => new Row { Entry = e }).ToList();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_plan is null) { _summary.Text = ""; _importButton.IsEnabled = false; return; }
        var included = _plan.Chapters.Where(c => c.Include && c.Error is null).ToList();
        var parts = new List<string> { $"{included.Count} chapter(s) to import, {_plan.TotalWords:N0} words" };
        if (_plan.MissingNumbers.Count > 0) parts.Add($"missing number(s): {string.Join(", ", _plan.MissingNumbers)}");
        if (_plan.DuplicateNumbers.Count > 0) parts.Add($"duplicate number(s): {string.Join(", ", _plan.DuplicateNumbers)}");
        if (_plan.MismatchCount > 0) parts.Add($"{_plan.MismatchCount} title(s) differ inside the file — choose which to use above");
        if (_plan.Skipped.Count > 0) parts.Add($"{_plan.Skipped.Count} other file(s) skipped: {string.Join(", ", _plan.Skipped.Take(4))}{(_plan.Skipped.Count > 4 ? "…" : "")}");
        _summary.Text = string.Join("  ·  ", parts);
        _importButton.IsEnabled = included.Count > 0;
    }

    private void DoImport()
    {
        if (_plan is null) return;
        Section? section = _sectionBox.SelectedItem as Section;
        if (ReferenceEquals(_sectionBox.SelectedItem, NewSection))
        {
            var name = _newSectionBox.Text.Trim();
            if (name.Length == 0) { MessageBox.Show(this, "Give the new section a name.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            section = new Section { Title = name };
            _book.Sections.Add(section);
        }

        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            Created = _importer.Import(_book, _plan, section);
            TargetSection = section;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Import stopped:\n\n{ex.Message}\n\nChapters imported before the error are kept.", "Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Cursor = null; }
    }
}
