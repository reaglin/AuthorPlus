using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Models;
using Eaglin.AiManager;
using Eaglin.AiManager.Wpf;

namespace AuthorPlus.App;

/// <summary>
/// Runs the chapter-analysis prompt on one provider or on every provider that has a key,
/// streaming each answer into an <see cref="AiRunPanel"/>, and turns each finished answer
/// into a dated Analysis item on the chapter. Closing the window mid-run stops the run;
/// answers already finished are kept.
/// </summary>
public sealed class AnalysisWindow : Window
{
    private const string Every = "Every provider with a key (compare)";

    private readonly AiHub _ai;
    private readonly Book _book;
    private readonly Chapter _chapter;
    private readonly Func<AiProviderType, AiRequest> _requestFor;
    private readonly ComboBox _providerBox = new() { MinWidth = 240 };
    private readonly Button _run = new() { Content = "Run", Width = 90, FontWeight = FontWeights.SemiBold };
    private readonly AiRunPanel _panel = new();
    private readonly TextBlock _progress = new() { Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

    /// <summary>Items added to the book by this window.</summary>
    public List<Item> Created { get; } = new();

    public AnalysisWindow(AiHub ai, Book book, Chapter chapter, Func<AiProviderType, AiRequest> requestFor)
    {
        _ai = ai; _book = book; _chapter = chapter; _requestFor = requestFor;
        Title = $"Analyze \"{chapter.Title}\"";
        Width = 860; Height = 640; MinWidth = 640; MinHeight = 420;
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
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var note = new TextBlock
        {
            Text = "Each finished answer is saved under the chapter as an Analysis item with the date, provider and model, so runs can be compared later. The prompt is \"chapter-analysis\" in AI › Prompt Library.",
            Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);
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
        if (providers.Count == 0) { MessageBox.Show(this, "No provider has a key. Open AI › AI Settings first.", "Analyze", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        _run.IsEnabled = false;
        _providerBox.IsEnabled = false;
        try
        {
            int n = 0;
            foreach (var p in providers)
            {
                n++;
                _progress.Text = providers.Count > 1 ? $"{n} of {providers.Count}: {AiHub.DisplayName(p)}" : AiHub.DisplayName(p);
                var response = await _panel.RunAsync(_requestFor(p));
                if (response is null)
                {
                    if (_panel.LastError is { } err)
                        _progress.Text = $"{AiHub.DisplayName(p)}: {err.Kind}";
                    else break;                                   // stopped by the user
                    continue;
                }
                var item = new Item
                {
                    OwnerId = _chapter.Id, Kind = ItemKind.Analysis,
                    Title = $"Analysis · {AiHub.DisplayName(p)} · {DateTime.Now:yyyy-MM-dd HH:mm}",
                    Body = response.Text.Trim(), Provider = p.ToString(), Model = response.Model, PromptName = AiPrompts.ChapterAnalysis
                };
                _book.Items.Add(item);
                Created.Add(item);
            }
            if (Created.Count > 0) _progress.Text = $"{Created.Count} analysis item(s) saved under the chapter.";
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
