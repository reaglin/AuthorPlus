﻿using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Models;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// "Ask again": the author sees the passage as it stands, the suggestion they did not take and
/// its reasoning, then says what they are actually trying to convey — in their own words, and by
/// picking the effect they are after. That goes back to the AI, which offers fresh rewrites
/// aimed at the author's intent rather than at its own idea of better prose.
/// </summary>
public sealed class SuggestionRequestWindow : Window
{
    /// <summary>Moods and effects an author reaches for. Picked ones go into the prompt as the effect wanted.</summary>
    public static readonly IReadOnlyList<string> Moods = new[]
    {
        "seriousness", "humour", "dry wit", "anger", "menace", "dread", "fear", "grief",
        "tenderness", "love", "hate", "warmth", "irony", "urgency", "calm", "wonder",
        "curiosity", "cold detachment", "hope", "regret", "resignation", "confusion",
        "relief", "suspicion", "intimacy"
    };

    /// <summary>Craft directions that sit beside the mood.</summary>
    public static readonly IReadOnlyList<string> Craft = new[]
    {
        "shorter and harder", "slower, let it breathe", "more concrete detail", "less explanation",
        "more subtext, say it sideways", "stronger verbs", "keep my rhythm", "more dialogue",
        "more silence", "closer point of view", "raise the stakes", "plainer language"
    };

    private readonly List<CheckBox> _moodBoxes = new();
    private readonly List<CheckBox> _craftBoxes = new();
    private readonly TextBox _request = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinLines = 5, Padding = new Thickness(8),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14
    };

    /// <summary>What the author says they are trying to convey.</summary>
    public string Request { get; private set; } = "";
    /// <summary>The effects they picked, comma separated ("menace, dry wit, shorter and harder").</summary>
    public string Intent { get; private set; } = "";

    /// <summary>"Ask again" about a suggestion the author has already been offered.</summary>
    public SuggestionRequestWindow(Chapter chapter, SuggestionEntry entry, string aspect)
        : this(chapter, entry, entry.Original, aspect) { }

    /// <summary>The first ask about a passage the author picked themselves — same form, nothing offered yet.</summary>
    public SuggestionRequestWindow(Chapter chapter, string passage, string aspect)
        : this(chapter, null, passage, aspect) { }

    private SuggestionRequestWindow(Chapter chapter, SuggestionEntry? entry, string passage, string aspect)
    {
        bool fresh = entry is null;
        Title = fresh ? $"Suggestions for this passage — {chapter.Title}" : $"Ask again — {chapter.Title}";
        Width = 900; Height = 760; MinWidth = 700; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };

        var head = new TextBlock
        {
            Text = "Tell the AI what this passage is for. What is the reader meant to feel, know or suspect here? What is the joke, the threat, the loss? " +
                   "Pick the effect you are after and say it in your own words; the rewrites will aim at that, in your voice." +
                   (fresh ? " You can also leave both blank and simply ask for rewrites." : " The next set will aim at that, in your voice."),
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        // Buttons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = fresh ? "Ask for suggestions" : "Ask for new suggestions", Padding = new Thickness(14, 4, 14, 4), IsDefault = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            Intent = string.Join(", ", _moodBoxes.Concat(_craftBoxes).Where(b => b.IsChecked == true).Select(b => (string)b.Content));
            Request = _request.Text.Trim();
            if (!fresh && Intent.Length == 0 && Request.Length == 0)
            {
                MessageBox.Show(this, "Pick an effect or write a line about what you are trying to convey — that is what makes this different from running Suggestions again.",
                    "Ask again", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var panel = new StackPanel();

        // The passage, and — when there is one — the suggestion that did not fit
        panel.Children.Add(Label(fresh ? "The passage you chose" : $"The passage as it stands ({aspect})"));
        panel.Children.Add(ReadOnly(passage, "Georgia"));
        if (entry != null)
        {
            panel.Children.Add(Label("The suggestion you were offered"));
            panel.Children.Add(ReadOnly(entry.Rewrite, "Georgia"));
            if (entry.Why.Length > 0)
            {
                panel.Children.Add(Label("Why it was suggested"));
                panel.Children.Add(ReadOnly(entry.Why, "Segoe UI"));
            }
            if (entry.AuthorRequest.Length > 0)
            {
                panel.Children.Add(Label("What you asked for last time"));
                panel.Children.Add(ReadOnly((entry.Intent.Length > 0 ? entry.Intent + " — " : "") + entry.AuthorRequest, "Segoe UI"));
            }
        }

        // Effect wanted
        panel.Children.Add(Label("The effect you want (pick any)"));
        panel.Children.Add(Chips(Moods, _moodBoxes));
        panel.Children.Add(Label("How it should read"));
        panel.Children.Add(Chips(Craft, _craftBoxes));

        // Author's own words
        panel.Children.Add(Label("What you are trying to convey (your words)"));
        _request.Text = entry?.AuthorRequest ?? "";
        panel.Children.Add(_request);
        panel.Children.Add(new TextBlock
        {
            Text = "For example: \"This is the moment Roland stops being a reporter and becomes a target — the reader should feel watched, not told he is in danger.\"",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), FontStyle = FontStyles.Italic
        });

        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Loaded += (_, _) => { _request.Focus(); _request.CaretIndex = _request.Text.Length; };
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 3) };

    private static TextBox ReadOnly(string text, string font) => new()
    {
        Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 150,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.WhiteSmoke,
        BorderThickness = new Thickness(1), Padding = new Thickness(8), FontFamily = new FontFamily(font), FontSize = 14
    };

    private static WrapPanel Chips(IReadOnlyList<string> options, List<CheckBox> into)
    {
        var wrap = new WrapPanel();
        foreach (var o in options)
        {
            var box = new CheckBox { Content = o, Margin = new Thickness(0, 3, 16, 3) };
            into.Add(box);
            wrap.Children.Add(box);
        }
        return wrap;
    }
}
