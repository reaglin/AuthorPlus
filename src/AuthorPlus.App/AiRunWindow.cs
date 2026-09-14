using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Models;
using Eaglin.AiManager;
using Eaglin.AiManager.Wpf;

namespace AuthorPlus.App;

/// <summary>
/// Runs one prompt on one provider or on every provider that has a key, streaming each answer
/// into an <see cref="AiRunPanel"/>, and turns each finished answer into a dated item on the
/// owner (a chapter, a section or the book). Used for chapter analysis, continuity checks,
/// plot analysis and the style read. Closing the window mid-run stops the run; answers already
/// finished are kept.
/// </summary>
public sealed class AiRunWindow : Window
{
    private const string Every = "Every provider with a key (compare)";

    private readonly AiHub _ai;
    private readonly Book _book;
    private readonly Guid _ownerId;
    private readonly ItemKind _kind;
    private readonly string _itemPrefix;
    private readonly string _promptName;
    private readonly Func<AiProviderType, AiRequest> _requestFor;
    private readonly Func<AiRequest, bool> _confirm;
    private readonly Action<AiResponse>? _handleResponse;
    private readonly Button _next;
    private readonly ComboBox _providerBox = new() { MinWidth = 240 };
    private readonly Button _run = new() { Content = "Run", Width = 90, FontWeight = FontWeights.SemiBold };
    private readonly AiRunPanel _panel = new();
    private readonly TextBlock _progress = new() { Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

    /// <summary>Items added to the book by this window.</summary>
    public List<Item> Created { get; } = new();

    /// <summary>True when the author pressed the "next step" button rather than closing.</summary>
    public bool NextRequested { get; private set; }

    /// <param name="requestFor">Builds the bound request for a provider (so the preview shows exactly what is sent).</param>
    /// <param name="confirm">The prompt-preview gate: returns false to skip sending.</param>
    /// <param name="nextLabel">The next step offered when the run finishes ("Next: review the suggestions →").</param>
    /// <param name="handleResponse">When set, the answer goes here instead of becoming a new item.</param>
    public AiRunWindow(AiHub ai, Book book, Guid ownerId, ItemKind kind, string title, string itemPrefix, string promptName, string note,
                       Func<AiProviderType, AiRequest> requestFor, Func<AiRequest, bool> confirm,
                       string nextLabel = "Next: review the result →", Action<AiResponse>? handleResponse = null)
    {
        _ai = ai; _book = book; _ownerId = ownerId; _kind = kind; _itemPrefix = itemPrefix; _promptName = promptName;
        _requestFor = requestFor; _confirm = confirm; _handleResponse = handleResponse;
        _next = new Button { Content = nextLabel, Padding = new Thickness(14, 4, 14, 4), IsEnabled = false, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
        Title = title;
        Width = 880; Height = 660; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;

        var items = new List<object>();
        items.AddRange(ai.ProvidersWithKeys.Select(p => new ProviderItem(p)));
        if (ai.ProvidersWithKeys.Count > 1) items.Add(Every);
        _providerBox.ItemsSource = items;
        _providerBox.SelectedItem = items.OfType<ProviderItem>().FirstOrDefault(i => i.Provider == ai.DefaultProvider) ?? items.FirstOrDefault();

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_run, Dock.Right);
        top.Children.Add(_run);
        top.Children.Add(new TextBlock { Text = "Provider:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        top.Children.Add(_providerBox);
        top.Children.Add(_progress);

        _panel.Hub = ai;
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var close = new Button { Content = "Close", Width = 90, IsCancel = true };
        _next.Click += (_, _) => { NextRequested = true; DialogResult = true; Close(); };
        bottom.Children.Add(close); bottom.Children.Add(_next);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var noteBlock = new TextBlock { Text = note, Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(noteBlock, Dock.Top);
        root.Children.Add(noteBlock);
        root.Children.Add(bottom);
        root.Children.Add(_panel);
        Content = root;

        _run.Click += async (_, _) => await RunAsync();
        Closing += (_, _) => _panel.Stop();
    }

    private async Task RunAsync()
    {
        if (_panel.IsRunning) return;
        var providers = ReferenceEquals(_providerBox.SelectedItem, Every)
            ? _ai.ProvidersWithKeys.ToList()
            : _providerBox.SelectedItem is ProviderItem pi ? new List<AiProviderType> { pi.Provider } : new List<AiProviderType>();
        if (providers.Count == 0) { MessageBox.Show(this, "No provider has a key. Open AI › AI Settings first.", Title, MessageBoxButton.OK, MessageBoxImage.Information); return; }

        _run.IsEnabled = false;
        _providerBox.IsEnabled = false;
        try
        {
            int n = 0;
            foreach (var p in providers)
            {
                n++;
                _progress.Text = providers.Count > 1 ? $"{n} of {providers.Count}: {AiHub.DisplayName(p)}" : AiHub.DisplayName(p);
                var request = _requestFor(p);
                if (!_confirm(request)) { _progress.Text = "Not sent."; continue; }
                var response = await _panel.RunAsync(request);
                if (response is null)
                {
                    if (_panel.LastError is { } err) { _progress.Text = $"{AiHub.DisplayName(p)}: {err.Kind}"; continue; }
                    break;                                   // stopped by the user
                }
                if (_handleResponse != null) { _handleResponse(response); _next.IsEnabled = true; _next.IsDefault = true; continue; }
                var item = new Item
                {
                    OwnerId = _ownerId, Kind = _kind,
                    Title = $"{_itemPrefix} · {AiHub.DisplayName(p)} · {DateTime.Now:yyyy-MM-dd HH:mm}",
                    Body = response.Text.Trim(), Provider = p.ToString(), Model = response.Model, PromptName = _promptName
                };
                _book.Items.Add(item);
                Created.Add(item);
                _next.IsEnabled = true;
                _next.IsDefault = true;
            }
            if (Created.Count > 0) _progress.Text = $"{Created.Count} item(s) saved — use the button below to go on.";
        }
        finally
        {
            _run.IsEnabled = true;
            _providerBox.IsEnabled = true;
        }
    }

    private sealed record ProviderItem(AiProviderType Provider)
    {
        public override string ToString() => AiHub.DisplayName(Provider);
    }
}

/// <summary>
/// "Preview AI prompts before sending": the exact system and user text, the provider and model,
/// a token estimate and the estimated input cost. Send or cancel.
/// </summary>
public sealed class PromptPreviewWindow : Window
{
    public bool Send { get; private set; }

    public PromptPreviewWindow(AiHub ai, AiRequest request)
    {
        var provider = request.Provider ?? ai.DefaultProvider;
        var model = string.IsNullOrWhiteSpace(request.Model) ? ai.DefaultModel(provider) : request.Model!;
        var tokens = AiHub.EstimateTokens(request.System) + AiHub.EstimateTokens(request.User);
        var cost = ai.EstimateInputCost(request, provider, model);

        Title = $"Prompt preview — {request.Purpose}";
        Width = 860; Height = 640; MinWidth = 600; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };
        var head = new TextBlock
        {
            Text = $"To: {AiHub.DisplayName(provider)} · {model}   ·   ≈ {tokens:N0} tokens in{(cost is { } c ? $" (≈ ${c:0.0000} before the answer)" : "")}   ·   max {request.MaxTokens:N0} tokens out\n" +
                   $"Prompt template: \"{request.Purpose}\" (editable in AI › Prompt Library). This is exactly what will be sent.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var send = new Button { Content = "Send", Width = 100, IsDefault = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Don't send", Width = 100, IsCancel = true };
        send.Click += (_, _) => { Send = true; DialogResult = true; Close(); };
        buttons.Children.Add(send); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        var sysLabel = new TextBlock { Text = "System", FontWeight = FontWeights.SemiBold };
        var sys = new TextBox { Text = request.System, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 2, 0, 8) };
        var userLabel = new TextBlock { Text = "User", FontWeight = FontWeights.SemiBold };
        var user = new TextBox { Text = request.User, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 2, 0, 0) };
        Grid.SetRow(sysLabel, 0); Grid.SetRow(sys, 1); Grid.SetRow(userLabel, 2); Grid.SetRow(user, 3);
        grid.Children.Add(sysLabel); grid.Children.Add(sys); grid.Children.Add(userLabel); grid.Children.Add(user);
        root.Children.Add(grid);
        Content = root;
    }
}

/// <summary>Style report for a chapter: the local statistics, and a button for the AI read.</summary>
public sealed class StyleWindow : Window
{
    public StyleWindow(string chapterTitle, Core.Services.Style.StyleReport report, Action aiRead, bool aiAvailable)
    {
        Title = $"Style — {chapterTitle}";
        Width = 760; Height = 640; MinWidth = 560; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var copy = new Button { Content = "Copy", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => { try { Clipboard.SetText(Core.Services.Style.StyleMetrics.Describe(report)); } catch { } };
        var read = new Button { Content = "AI voice read…", Padding = new Thickness(12, 4, 12, 4), IsEnabled = aiAvailable, ToolTip = aiAvailable ? "Ask the AI to read the chapter with these statistics as leads" : "Add an API key in AI › AI Settings first", Margin = new Thickness(0, 0, 8, 0) };
        read.Click += (_, _) => { Close(); aiRead(); };
        var close = new Button { Content = "Close", Width = 90, IsCancel = true };
        buttons.Children.Add(copy); buttons.Children.Add(read); buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var help = new TextBlock { Text = "Computed locally in an instant, no AI. Passive voice is a heuristic (an auxiliary followed by a past participle): treat it as places to look, not a verdict.", Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(help, Dock.Top);
        root.Children.Add(help);

        var b = report.SentenceLengthBuckets;
        var rows = new (string, string)[]
        {
            ("Words", $"{report.Words:N0} in {report.Sentences:N0} sentences, {report.Paragraphs:N0} paragraphs"),
            ("Average sentence", $"{report.AverageSentenceLength} words (longest {report.LongestSentenceWords})"),
            ("Sentence lengths", $"≤10: {b.ElementAtOrDefault(0)}   11–20: {b.ElementAtOrDefault(1)}   21–30: {b.ElementAtOrDefault(2)}   31–40: {b.ElementAtOrDefault(3)}   41+: {b.ElementAtOrDefault(4)}"),
            ("Paragraphs", $"{report.AverageParagraphWords} words on average"),
            ("Passive voice", $"{report.PassiveSentences} sentences ({report.PassivePercent}%)"),
            ("Adverbs (-ly)", $"{report.Adverbs} ({report.AdverbsPerHundredWords} per 100 words)"),
            ("Dialogue", $"{report.DialoguePercent}% of the text"),
            ("Reading ease", $"{report.FleschReadingEase} — {report.FleschLabel}"),
            ("Most used words", string.Join(", ", report.TopWords.Take(12).Select(t => $"{t.Word} ×{t.Count}"))),
            ("Repeated openings", report.RepeatedSentenceStarts.Count == 0 ? "none" : string.Join(", ", report.RepeatedSentenceStarts)),
            ("Longest sentence", report.LongestSentence)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int r = 0;
        foreach (var (label, value) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 16, 4), VerticalAlignment = VerticalAlignment.Top };
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(l, r); Grid.SetColumn(l, 0); Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            grid.Children.Add(l); grid.Children.Add(v);
            r++;
        }
        root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }
}
