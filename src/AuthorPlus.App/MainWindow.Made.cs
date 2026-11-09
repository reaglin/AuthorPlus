using System.ComponentModel;
using System.Diagnostics;
using System.IO;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;

namespace AuthorPlus.App;

/// <summary>
/// Generated Audio/Video (Ron, 2026-11-09; plan 11.6): one page in the tree with everything made from the book —
/// the videos for YouTube, the exported audiobooks and each chapter's audiobook track — to view or listen to.
/// Videos and audiobooks are listed from <see cref="MadeMedia"/> (they can be saved anywhere); tracks from the book's audio\chapters.
/// </summary>
public partial class MainWindow
{
    private const string GeneratedHeader = "Generated Audio/Video";
    /// <summary>The track playing from this page (its id), or null.</summary>
    private string? _genPlaying;

    /// <summary>The chapter tracks that are made, in the book's order: the opening credits, the chapters, the closing credits.</summary>
    private List<(string Id, string Label, Chapter? Chapter, TrackNote Note)> MadeTracks()
    {
        var list = new List<(string, string, Chapter?, TrackNote)>();
        if (_book?.FolderPath == null || !Directory.Exists(AudioFolder("chapters"))) return list;
        void Add(string id, string label, Chapter? ch) { if (TrackNoteOf(id) is { } n) list.Add((id, label, ch, n)); }
        Add("opening", "Opening credits", null);
        foreach (var ch in _book.Chapters)                                 // chapter numbers start again in each part, so the part is named
            Add(ch.Id.ToString("N"), (_book.SectionOf(ch) is { } sec ? sec.Title + " · " : "") + $"Chapter {_book.ChapterNumber(ch)}. {ch.Title}", ch);
        Add("closing", "Closing credits", null);
        return list;
    }

    private int GeneratedCount()
    {
        if (_book?.FolderPath == null) return 0;
        int tracks = 0;
        try
        {
            if (Directory.Exists(AudioFolder("chapters")))
            {
                var ids = _book.Chapters.Select(c => c.Id.ToString("N")).Append("opening").Append("closing").ToHashSet();
                tracks = Directory.GetFiles(AudioFolder("chapters"), "*.json").Count(f => ids.Contains(Path.GetFileNameWithoutExtension(f)) && File.Exists(Path.ChangeExtension(f, ".mp3")));
            }
        }
        catch (IOException) { }
        return MadeMedia.Load(_book.FolderPath).Count + tracks;
    }

    private UIElement BuildGeneratedPage()
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 16), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock
        {
            Text = "Everything made from this book, to watch or listen to: the videos for YouTube, the audiobooks you exported, and each chapter's audiobook track. " +
                   "To make more: in Listen mode a chapter's Audiobook card makes its track and its video; File ▸ Export ▸ Audiobook makes the whole audiobook, and a video of the whole book.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8)
        });
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(PanelButton("Export audiobook / video of the book…", "Opens File ▸ Export ▸ Audiobook: the whole audiobook (MP3s and one M4B) and a video of it for YouTube",
                                     () => { ExportAudiobook_Click(this, new RoutedEventArgs()); _madeWhy = null; ShowCurrent(); }));
        top.Children.Add(PanelButton("Open the videos folder", "Where videos are saved unless you chose another place", () => OpenFolder(VideosFolder(_book!))));
        panel.Children.Add(top);

        try { MadeMedia.FindOlder(_book!.FolderPath!, new[] { VideosFolder(_book) }, AudiobookFolders()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var made = MadeMedia.Load(_book!.FolderPath!);
        var videos = made.Where(m => m.Kind == MadeKind.Video).ToList();
        var books = made.Where(m => m.Kind == MadeKind.Audiobook).ToList();
        var tracks = MadeTracks();

        panel.Children.Add(Heading($"Videos ({videos.Count})"));
        if (videos.Count == 0) panel.Children.Add(Hint("No video yet. In Listen mode, once a chapter's audiobook track is made, its Audiobook card has 🎬 Make a video…; File ▸ Export ▸ Audiobook makes one of the whole book."));
        foreach (var v in videos) panel.Children.Add(MadeRow(v));

        panel.Children.Add(Heading($"Audiobooks ({books.Count})"));
        if (books.Count == 0) panel.Children.Add(Hint("No audiobook exported yet. File ▸ Export ▸ Audiobook saves the chapters as numbered MP3s and the whole book as one M4B."));
        foreach (var b in books) panel.Children.Add(MadeRow(b));

        panel.Children.Add(Heading($"Chapter tracks ({tracks.Count})"));
        if (tracks.Count == 0) panel.Children.Add(Hint("No chapter track yet. In Listen mode, approve a chapter for the audiobook to make its track."));
        else panel.Children.Add(Hint("Each chapter's audiobook track as it was made. Listen plays it here (Stop, or choosing another page, stops it). " +
                                     "A track is marked out of date when its chapter has changed since it was made — Go to the chapter opens it in Listen mode, where its Audiobook card makes it again. " +
                                     "Approving a chapter there says you have listened to it and it is right; the audiobook export can then take only the approved chapters."));
        _madeWhy ??= tracks.Where(t => t.Chapter != null).ToDictionary(t => t.Id, t => _book.NarratorVoice is { } v ? WhyRemake(ChapterPlan(t.Chapter!, v)) : null);
        foreach (var t in tracks) panel.Children.Add(TrackRow(t.Id, t.Label, t.Chapter, t.Note));

        // Playing, stopping or removing redraws the page: it stays where the author was.
        var sv = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var keep = _madeScroll;
        sv.Loaded += (_, _) => sv.ScrollToVerticalOffset(keep);
        sv.ScrollChanged += (_, _) => { if (sv.IsLoaded) _madeScroll = sv.VerticalOffset; };
        return sv;
    }

    private double _madeScroll;

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 14, 0, 6) };

    private static string PlayLength(double seconds) =>
        seconds <= 0 ? "" : TimeSpan.FromSeconds(seconds) is var t && t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t:mm\\:ss}" : $"{t:m\\:ss}";

    /// <summary>A video or an audiobook on the list: what it is, and View/Listen, its folder, or off the list.</summary>
    private Border MadeRow(MadeFile m)
    {
        bool there = m.IsThere;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (m.Kind == MadeKind.Video && there && m.Thumbnail is { } thumb && File.Exists(thumb))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = 320; bmp.UriSource = new Uri(thumb); bmp.EndInit();   // not kept open
                var img = new Image { Source = bmp, Width = 160, Height = 90, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "View the video" };
                img.MouseLeftButtonUp += (_, _) => OpenInPlayer(m.Path);
                grid.Children.Add(img);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException) { }
        }
        else if (m.Kind == MadeKind.Video && there)                         // no thumbnail: a play box, so the rows line up
        {
            var box = new Border { Width = 160, Height = 90, Margin = new Thickness(0, 0, 12, 0), Background = Palette.HeaderBg, BorderBrush = Palette.Line, BorderThickness = new Thickness(1),
                                   VerticalAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "View the video",
                                   Child = new TextBlock { Text = "▶", FontSize = 28, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            box.MouseLeftButtonUp += (_, _) => OpenInPlayer(m.Path);
            grid.Children.Add(box);
        }
        var info = new StackPanel();
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        info.Children.Add(new TextBlock { Text = m.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var facts = new List<string> { "Made " + m.MadeUtc.ToLocalTime().ToString("d MMM yyyy, h:mm tt") };
        if (PlayLength(m.Seconds) is { Length: > 0 } len) facts.Add(len);
        if (m.Kind == MadeKind.Audiobook && m.Tracks > 0) facts.Add($"{m.Tracks} tracks" + (m.Listen != null ? " and one M4B" : ""));
        if (m.Kind == MadeKind.Video && there) try { facts.Add($"{new FileInfo(m.Path).Length / 1048576.0:0.0} MB"); } catch (IOException) { }
        info.Children.Add(Body(string.Join(" · ", facts), muted: true));
        info.Children.Add(new TextBlock { Text = m.Path, Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        if (!there)
            info.Children.Add(new TextBlock { Text = (m.Kind == MadeKind.Video ? "The video is not there any more" : "The folder is not there any more") + " — it was moved or deleted. Remove it from the list, or make it again.",
                                              Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

        var buttons = new WrapPanel();
        info.Children.Add(buttons);
        if (there && m.Kind == MadeKind.Video)
        {
            buttons.Children.Add(PanelButton("▶ View", "Plays the video in your video player", () => OpenInPlayer(m.Path), primary: true));
            if (m.Description is { } d && File.Exists(d))
                buttons.Children.Add(PanelButton("Copy the YouTube description", "Copies the description saved beside the video, ready to paste into YouTube", () =>
                {
                    try { Clipboard.SetText(File.ReadAllText(d)); UpdateStatus("The YouTube description is copied — paste it into YouTube's Description box."); }
                    catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException) { UpdateStatus("It could not be copied: " + ex.Message); }
                }));
            buttons.Children.Add(PanelButton("Show in folder", "Opens the folder with the video selected (its thumbnail and description are beside it)", () => ShowInFolder(m.Path)));
        }
        else if (there)
        {
            if (m.Listen is { } m4b && File.Exists(m4b))
                buttons.Children.Add(PanelButton("▶ Listen", "Plays the whole audiobook (the M4B, with its chapters) in your audio player", () => OpenInPlayer(m4b), primary: true));
            buttons.Children.Add(PanelButton("Open the folder", "The numbered MP3s for the stores, the M4B, the chapter list and the note on where to publish", () => OpenFolder(m.Path),
                                             primary: m.Listen == null || !File.Exists(m.Listen)));
        }
        buttons.Children.Add(PanelButton("Remove from the list", "Takes it off this page; the files themselves are not deleted", () =>
        {
            try { MadeMedia.Forget(_book!.FolderPath!, m); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { UpdateStatus("The list could not be changed: " + ex.Message); }
            RefreshTreeTexts(); ShowCurrent();
        }));
        return RowBorder(grid);
    }

    /// <summary>A made chapter track: Listen here, go to its chapter, or save its MP3.</summary>
    private Border TrackRow(string id, string label, Chapter? ch, TrackNote note)
    {
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var facts = new List<string>();
        if (PlayLength(note.Seconds) is { Length: > 0 } len) facts.Add(len);
        try { facts.Add("made " + File.GetLastWriteTime(TrackMp3(id)).ToString("d MMM yyyy")); } catch (IOException) { }
        if (ch != null) facts.Add(note.Approved ? "approved for the audiobook" : "not approved yet");   // the credits are not approved, only made
        var factLine = Body(string.Join(" · ", facts), muted: true);
        if (ch != null) factLine.ToolTip = "Approve a chapter in Listen mode once you have listened to it and it is right; the audiobook export can then take only the approved chapters";
        info.Children.Add(factLine);
        if (ch != null && _madeWhy != null && _madeWhy.TryGetValue(id, out var why) && why != null)
            info.Children.Add(new TextBlock { Text = $"Out of date: {why}. Go to the chapter to hear it as it is now and make the track again.", Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (ch == null)
            info.Children.Add(Body("Made with the audiobook, from the credits in File ▸ Export ▸ Audiobook (change them there; the next export makes them again).", muted: true));
        bool playing = _genPlaying == id && _playCts != null;
        if (playing) info.Children.Add(new TextBlock { Text = "Playing…", Foreground = Palette.Link, Margin = new Thickness(0, 2, 0, 0) });
        var buttons = new WrapPanel();
        info.Children.Add(buttons);
        buttons.Children.Add(playing
            ? PanelButton("■ Stop", "Stops playing", () => { StopListening(); _genPlaying = null; ShowCurrent(); }, primary: true)
            : PanelButton("▶ Listen", "Plays this track here, as the audiobook has it", () => PlayGenerated(id, label), primary: true));
        if (ch != null)
            buttons.Children.Add(PanelButton("Go to the chapter", "Opens the chapter in Listen mode, with its Audiobook card (approve, make a video)", () =>
            {
                StopListening(); _genPlaying = null;
                SetMode(EditorMode.Listen);
                SelectNode(ch);
            }));
        if (ch == null)
            buttons.Children.Add(PanelButton("Export audiobook…", "Opens File ▸ Export ▸ Audiobook, where the opening and closing credits are written and made", () =>
            { StopListening(); _genPlaying = null; ExportAudiobook_Click(this, new RoutedEventArgs()); _madeWhy = null; ShowCurrent(); }));
        buttons.Children.Add(PanelButton("Save the MP3…", "Saves a copy of this track where you choose", () =>
        {
            var name = label;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = name + ".mp3", Filter = "MP3 audio (*.mp3)|*.mp3", Title = "Save this track's MP3" };
            if (dlg.ShowDialog(this) != true) return;
            try { File.Copy(TrackMp3(id), dlg.FileName, overwrite: true); UpdateStatus("Saved: " + dlg.FileName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, "It could not be saved there: " + ex.Message, "Save the MP3", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }));
        return RowBorder(info);
    }

    private static Border RowBorder(UIElement child) => new()
    {
        Background = Palette.RowAltBg, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
        Padding = new Thickness(10, 8, 10, 10), Margin = new Thickness(0, 0, 0, 8), Child = child
    };

    private async void PlayGenerated(string id, string label)
    {
        StopListening();
        var cts = new CancellationTokenSource();
        _playCts = cts; _genPlaying = id;
        UpdateStatus($"Playing {label}…");
        ShowCurrent();
        try { await PlayFile(TrackMp3(id), cts.Token); }
        catch (OperationCanceledException) { }
        catch (PlaybackException ex) { MessageBox.Show(this, ex.Message, GeneratedHeader, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally
        {
            if (ReferenceEquals(_playCts, cts))
            {
                _playCts = null; _genPlaying = null;
                if (Equals(_current, GeneratedHeader)) ShowCurrent();
            }
            cts.Dispose();
        }
    }

    /// <summary>Leaving the page stops a track it is playing.</summary>
    private void LeaveGenerated()
    {
        if (Equals(_current, GeneratedHeader)) return;
        _madeWhy = null;                                                   // worked out again on the next visit: the chapters may change meanwhile
        if (_genPlaying != null) { StopListening(); _genPlaying = null; }
    }

    /// <summary>Why each chapter's track would be made again (null: it is current), worked out once per visit to the page.</summary>
    private Dictionary<string, string?>? _madeWhy;

    /// <summary>Where this book's audiobooks are saved unless the author chose otherwise (the whole book, and a part's own folder).</summary>
    private IEnumerable<string> AudiobookFolders()
    {
        if (_book!.AudiobookFolder.Length > 0) yield return _book.AudiobookFolder;
        var name = _book.Title;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().Length > 0 ? name.Trim() : "Audiobook";            // as the export names it
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AuthorPlus", "Audiobooks");
        yield return Path.Combine(root, name);
        if (Directory.Exists(root)) foreach (var d in Directory.GetDirectories(root, name + " — *")) yield return d;
    }

    /// <summary>Opens a video or an M4B in the program Windows uses for it; asks which when there is none.</summary>
    private void OpenInPlayer(string path)
    {
        StopListening(); _genPlaying = null;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Win32Exception)
        {
            try { Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}")); }
            catch (Win32Exception ex) { MessageBox.Show(this, "No program on this computer opens it: " + ex.Message, GeneratedHeader, MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }

    private static void ShowInFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Win32Exception) { }
    }

    private static void OpenFolder(string dir)
    {
        try { Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException) { }
    }
}
