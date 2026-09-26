using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using ImageBrush = System.Windows.Media.ImageBrush;
using Stretch = System.Windows.Media.Stretch;

namespace AuthorPlus.App;

/// <summary>
/// Help › About AuthorPlus: who made it (Ron, 2026-09-25: "Created by Dr. Ron Eaglin" with his
/// picture, in every program's About), what the program is for, the version — read from the
/// assembly, which takes it from <c>AuthorPlusVersion</c> in Directory.Build.props — and where the
/// books are kept.
/// </summary>
public sealed class AboutWindow : Window
{
    public AboutWindow(string booksFolder)
    {
        Title = "About AuthorPlus";
        Width = 520; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new StackPanel { Margin = new Thickness(16) };

        // The picture on the left, the heading and the version beside it.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var picture = new Border
        {
            Width = 96, Height = 96, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 16, 0),
            BorderBrush = Palette.Line, BorderThickness = new Thickness(1),
            Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Images/dr-ron-eaglin.jpg")))
            {
                Stretch = Stretch.UniformToFill
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(picture, "Photograph of Dr. Ron Eaglin");
        DockPanel.SetDock(picture, Dock.Left);
        header.Children.Add(picture);

        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = "Created by Dr. Ron Eaglin", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4)
        });
        heading.Children.Add(new TextBlock { Text = $"AuthorPlus, version {Version}", Foreground = Brushes.Gray });
        header.Children.Add(heading);
        root.Children.Add(header);

        foreach (var paragraph in Description)
            root.Children.Add(new TextBlock { Text = paragraph, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        root.Children.Add(new TextBlock { Text = "Your books are kept in", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        root.Children.Add(new TextBlock { Text = booksFolder, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray });

        var bottom = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Close", Width = 80, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => Close();
        DockPanel.SetDock(ok, Dock.Right);
        bottom.Children.Add(ok);
        bottom.Children.Add(new TextBlock { Text = "© Ron Eaglin", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(bottom);

        Content = root;
    }

    /// <summary>What AuthorPlus is, for someone who has just opened it — two short paragraphs.</summary>
    internal static readonly string[] Description =
    [
        "AuthorPlus helps you write a book and keep it straight. It holds your chapters, characters, timeline and plotlines in one tree, and you write each chapter in its own editor.",
        "Its AI features, using your own API key, summarise a chapter, read it for pacing and tension, and suggest rewrites of a passage you choose. Your book stays on your computer as a folder of ordinary files, and can keep its own saved versions."
    ];

    /// <summary>The version from the assembly (major.minor.patch), never typed in by hand.</summary>
    internal static string Version =>
        typeof(AboutWindow).Assembly.GetName().Version is { } v ? v.ToString(3) : "unknown";
}
