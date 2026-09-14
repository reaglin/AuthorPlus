﻿using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Services;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// One long field of a character or a plotline, shown as bullet points rather than a wall of text.
/// Each entry is a line; a line the AI added carries an "(AI)" mark and a tint, so the author can
/// always see what came from them and what came from the machine. <b>Edit</b> opens the whole
/// field in one box — every entry at once, the marks included — because the author's own words
/// govern and they must never have to fight the shape of the form to write them.
/// </summary>
public sealed class BulletField : DockPanel
{
    private readonly StackPanel _list = new();
    private readonly Func<string> _get;
    private readonly Action<string> _set;
    private readonly Action _changed;
    private readonly string _label;
    private readonly string _hint;
    private readonly Window? _owner;

    public BulletField(string label, Func<string> get, Action<string> set, Action changed, string hint = "", Window? owner = null)
    {
        _label = label; _get = get; _set = set; _changed = changed; _hint = hint; _owner = owner;
        Margin = new Thickness(0, 12, 0, 0);

        var header = new DockPanel();
        var edit = new Button { Content = "Edit", Padding = new Thickness(12, 2, 12, 2), ToolTip = $"Edit the whole of \"{label}\" in your own words" };
        edit.Click += (_, _) => Edit();
        SetDock(edit, Dock.Right);
        header.Children.Add(edit);
        header.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        SetDock(header, Dock.Top);
        Children.Add(header);

        _list.Margin = new Thickness(0, 4, 0, 0);
        Children.Add(_list);
        Paint();
    }

    /// <summary>Rebuilds the bullets from the field as it stands (after an edit, or after the AI adds to it).</summary>
    public void Paint()
    {
        _list.Children.Clear();
        var bullets = Bullets.Parse(_get());
        if (bullets.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = _hint.Length > 0 ? _hint : "— nothing here yet —", Foreground = Brushes.Gray, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 2, 0, 2) });
            return;
        }
        foreach (var bullet in bullets)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var dot = new TextBlock { Text = "•", Foreground = Brushes.Gray, FontSize = 16, Margin = new Thickness(4, -2, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            SetDock(dot, Dock.Left);
            row.Children.Add(dot);

            if (bullet.FromAi)
            {
                var mark = Palette.Chip("AI", Palette.AiBg, "The AI added this. Edit the field to change it, take the (AI) mark off, or delete the line.");
                mark.Margin = new Thickness(0, 0, 8, 0);
                mark.VerticalAlignment = VerticalAlignment.Top;
                SetDock(mark, Dock.Left);
                row.Children.Add(mark);
            }
            row.Children.Add(new TextBlock
            {
                Text = bullet.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14,
                Background = bullet.FromAi ? Palette.AiRowBg : Brushes.Transparent, Padding = new Thickness(bullet.FromAi ? 4 : 0, 1, 4, 2)
            });
            _list.Children.Add(row);
        }
    }

    private void Edit()
    {
        var dlg = new FieldEditWindow(_label, _get(), _hint) { Owner = _owner ?? Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _set(dlg.Text);
        Paint();
        _changed();
    }
}

/// <summary>The whole of one field, in one box. One entry per line; a line starting "(AI)" came from the AI.</summary>
public sealed class FieldEditWindow : Window
{
    private readonly TextBox _box;

    /// <summary>The field as the author left it.</summary>
    public string Text => _box.Text.Trim();

    public FieldEditWindow(string label, string text, string hint)
    {
        Title = $"Edit — {label}";
        Width = 760; Height = 520; MinWidth = 480; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };
        var head = new TextBlock
        {
            Text = (hint.Length > 0 ? hint + "  " : "") + "One entry per line. A line that starts with \"(AI)\" was written by the AI — change it, delete it, or take the mark off to make it yours.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Save", Width = 100, IsDefault = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 100, IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _box = new TextBox
        {
            Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8), FontSize = 14, SpellCheck = { IsEnabled = true }
        };
        root.Children.Add(_box);
        Content = root;
        Loaded += (_, _) => { _box.Focus(); _box.CaretIndex = _box.Text.Length; };
    }
}
