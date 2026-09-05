using System.Windows;
using System.Windows.Controls;

namespace AuthorPlus.App;

/// <summary>Small modal dialog with one to four labelled text fields (code-only, no XAML).</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox[] _boxes;

    public string Value1 { get => _boxes[0].Text; set => _boxes[0].Text = value; }
    public string Value2 { get => _boxes.Length > 1 ? _boxes[1].Text : ""; set { if (_boxes.Length > 1) _boxes[1].Text = value; } }
    public string Value3 { get => _boxes.Length > 2 ? _boxes[2].Text : ""; set { if (_boxes.Length > 2) _boxes[2].Text = value; } }
    public string Value4 { get => _boxes.Length > 3 ? _boxes[3].Text : ""; set { if (_boxes.Length > 3) _boxes[3].Text = value; } }

    public PromptWindow(string title, params string[] labels)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;

        var panel = new StackPanel { Margin = new Thickness(16) };
        _boxes = new TextBox[Math.Max(1, labels.Length)];
        for (int i = 0; i < _boxes.Length; i++)
        {
            var label = i < labels.Length ? labels[i] : "Value:";
            bool multi = label.StartsWith("Synopsis", StringComparison.OrdinalIgnoreCase);
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 2) });
            _boxes[i] = new TextBox
            {
                Padding = new Thickness(4),
                AcceptsReturn = multi,
                TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinLines = multi ? 3 : 1,
                MaxLines = multi ? 6 : 1,
                VerticalScrollBarVisibility = multi ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
            };
            panel.Children.Add(_boxes[i]);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => { _boxes[0].Focus(); _boxes[0].SelectAll(); };
    }
}
