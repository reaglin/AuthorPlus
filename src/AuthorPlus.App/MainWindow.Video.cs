using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services.Audio;
using AuthorPlus.Core.Services.Video;
using NAudio.Wave;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;

namespace AuthorPlus.App;

/// <summary>
/// The audiobook as a video for YouTube (Ron, 2026-11-09: <i>"Under listen I would also like to add Video. The video
/// would simply be each paragraph on the video screen with the narration."</i>; plan phase 11). A video is made
/// from audiobook tracks already made — nothing is sent anywhere or paid for — on this computer, with Windows'
/// own encoders: each paragraph on the screen while it is read, the chapter's title while it is announced,
/// the credits with the cover. Beside it: a thumbnail and the description to paste into YouTube.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// When each piece of a made track is heard: the times noted when it was made; for a track made before videos,
    /// worked out from its length and put right by its pauses, then noted with it so it is done once.
    /// No WPF here — it runs off the window's thread.
    /// </summary>
    private List<HeardPiece> HeardOf(TrackPlan plan)
    {
        var note = TrackNoteOf(plan.Id) ?? throw new InvalidDataException($"{plan.Label}: its audiobook track is not made.");
        if (note.PieceStarts.Count == plan.Pieces.Count && note.PieceEnds.Count == plan.Pieces.Count)
            return plan.Pieces.Select((p, i) => new HeardPiece(p.Paragraph, note.PieceStarts[i], note.PieceEnds[i])).ToList();
        var est = VideoScreens.Estimate(plan.Pieces.Select(p => (p.Paragraph, p.Spoken.Text.Length, p.PauseMs)).ToList(), note.Seconds);
        var heard = VideoScreens.Snap(est, ChapterSound.PauseEnds(ReadMono(TrackMp3(plan.Id))));
        if (note.Fingerprint == plan.Fingerprint)
            try
            {
                note.PieceStarts = heard.Select(h => Math.Round(h.Start, 3)).ToList();
                note.PieceEnds = heard.Select(h => Math.Round(h.End, 3)).ToList();
                File.WriteAllText(TrackNotePath(plan.Id), System.Text.Json.JsonSerializer.Serialize(note));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }      // worked out again next time
        return heard;
    }

    /// <summary>The screens of a made track (WPF measures the text, so on the window's thread).</summary>
    private VideoPart VideoPartOf(TrackPlan plan, List<HeardPiece> heard, VideoPainter measure)
    {
        var note = TrackNoteOf(plan.Id) ?? throw new InvalidDataException($"{plan.Label}: its audiobook track is not made.");

        var ch = _book!.Chapters.FirstOrDefault(c => c.Id.ToString("N") == plan.Id);
        string header, title;
        ScreenKind kind;
        List<string> paras;
        if (ch != null)
        {
            var section = _book.SectionOf(ch);
            bool firstOfSection = section != null && _book.Chapters.FirstOrDefault(c => c.SectionId == section.Id) == ch;
            header = $"{_book.Title}  ·  Chapter {_book.ChapterNumber(ch)}. {ch.Title}";
            title = (firstOfSection ? section!.Title + "\n" : "") + $"Chapter {_book.ChapterNumber(ch)}\n{ch.Title}";
            kind = ScreenKind.Title;
            paras = ChapterParagraphs(ch);
        }
        else
        {
            var by = _book.Author.Length > 0 ? $"Written by {_book.Author}\n" : "";
            header = _book.Title;
            title = plan.Id == "closing" ? $"The End\n{_book.Title}\n{by}Narrated by a synthesized voice\nMade with Author+"
                                         : $"{_book.Title}\n{by}Narrated by a synthesized voice";
            kind = ScreenKind.Credits;
            paras = new();
        }
        var screens = VideoScreens.ForTrack(heard, paras, title, kind, note.Seconds, measure.Fits);
        return new VideoPart(TrackMp3(plan.Id), header, screens, note.Seconds);
    }

    /// <summary>An MP3 as one channel of sound (to find its pauses).</summary>
    private static PcmAudio ReadMono(string mp3)
    {
        using var reader = new MediaFoundationReader(mp3);
        var samples = reader.ToSampleProvider().ToMono();
        var list = new List<short>();
        var buf = new float[reader.WaveFormat.SampleRate];
        int n;
        while ((n = samples.Read(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < n; i++) list.Add((short)Math.Clamp(buf[i] * 32767, -32768, 32767));
        return new PcmAudio(reader.WaveFormat.SampleRate, list.ToArray());
    }

    private static string VideosFolder(Book book)
    {
        var name = book.Title;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AuthorPlus", "Videos", name.Trim().Length > 0 ? name.Trim() : "Book");
    }

    /// <summary>Listen ▸ Audiobook ▸ Make a video: this chapter's track as a video.</summary>
    private void MakeChapterVideo(Chapter ch, TrackPlan plan)
    {
        StopListening();
        CommitCurrent();
        var name = $"{_book!.ChapterNumber(ch):00} {ch.Title}";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        new VideoWindow(this, $"Chapter {_book.ChapterNumber(ch)}. {ch.Title}", new List<TrackPlan> { plan }, Path.Combine(VideosFolder(_book), name + ".mp4"), wholeBook: false).ShowDialog();
        if (_mode == EditorMode.Listen && _side != null && ReferenceEquals(_current, ch)) ShowSidePanel(keepScroll: true);     // a damaged track now says so
    }

    /// <summary>The Make a video window: the look (with a picture of it), where to save, then the making.</summary>
    private sealed class VideoWindow : Window
    {
        private readonly MainWindow _w;
        private readonly Book _book;
        private readonly List<TrackPlan> _tracks;
        private readonly bool _wholeBook;
        private readonly string _what;
        private readonly RadioButton _light, _dark;
        private readonly Image _preview = new() { Width = 480, Height = 270, Stretch = System.Windows.Media.Stretch.Uniform };
        private readonly TextBox _path;
        private readonly TextBlock _progressText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        private readonly ProgressBar _progress = new() { Height = 14, Margin = new Thickness(0, 8, 0, 0), Maximum = 1 };
        private readonly Button _go, _stop, _open;
        private CancellationTokenSource? _cts;
        private List<VideoPart>? _parts;
        private double _seconds;

        public VideoWindow(MainWindow w, string what, List<TrackPlan> tracks, string defaultPath, bool wholeBook)
        {
            _w = w; _book = w._book!; _tracks = tracks; _wholeBook = wholeBook; _what = what;
            Owner = w; Title = $"Make a video — {what}";
            Width = 780; Height = Math.Min(800, SystemParameters.WorkArea.Height - 40); MinWidth = 600; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;
            _seconds = tracks.Sum(t => w.TrackNoteOf(t.Id)?.Seconds ?? 0);

            var root = new DockPanel { Margin = new Thickness(16) };
            var bottom = new StackPanel();
            bottom.Children.Add(_progress);
            bottom.Children.Add(_progressText);
            var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            _go = new Button { Content = "Make the video", Padding = new Thickness(14, 5, 14, 5), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), IsDefault = true, IsEnabled = false,
                               ToolTip = "Makes the video on this computer from the audiobook track(s) — nothing is sent anywhere or paid for" };
            ToolTipService.SetShowOnDisabled(_go, true);
            _stop = new Button { Content = "Stop", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false, ToolTip = "Stops making the video; no half-made file is left" };
            ToolTipService.SetShowOnDisabled(_stop, true);
            _open = new Button { Content = "Open the folder", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Opens the folder the video is saved in" };
            var close = new Button { Content = "Close", Padding = new Thickness(14, 5, 14, 5), IsCancel = true };
            _go.Click += async (_, _) => await Make();
            _stop.Click += (_, _) => { _cts?.Cancel(); _stop.IsEnabled = false; _progressText.Text = "Stopping…"; };
            _open.Click += (_, _) => OpenFolder();
            close.Click += (_, _) => Close();
            Closing += (_, e) =>
            {
                if (_cts == null) return;
                if (MessageBox.Show(this, "The video is still being made. Stop and close? (Nothing is lost: making it again starts over, and costs nothing.)", Title,
                                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
                else _cts.Cancel();
            };
            buttons.Children.Add(_go); buttons.Children.Add(_stop); buttons.Children.Add(_open); buttons.Children.Add(close);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "Make a video for YouTube", FontSize = 18, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock
            {
                Text = (wholeBook ? $"The audiobook you made — {Chapters(tracks)}, {Length(_seconds)} — as one video" : $"This chapter's audiobook track ({Length(_seconds)}) as a video") +
                       ": each paragraph on the screen while it is read, the chapter's title while it is announced" + (wholeBook ? ", the credits with the book's cover" : "") + ". " +
                       "HD (1920 × 1080) MP4, the sound at 192 kbps. It is made on this computer from the track" + (wholeBook ? "s" : "") + " already made: nothing is sent anywhere and nothing is paid for. " +
                       $"It takes about {Making(_seconds)}.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 12)
            });

            var look = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            look.Children.Add(new TextBlock { Text = "Look:", Width = 70, VerticalAlignment = VerticalAlignment.Center });
            _light = new RadioButton { Content = "Light page (dark ink on cream)", GroupName = "look", Margin = new Thickness(0, 2, 16, 2), IsChecked = _book.VideoLook != "Dark", VerticalAlignment = VerticalAlignment.Center };
            _dark = new RadioButton { Content = "Dark page (light ink on black — easier on the eyes at night)", GroupName = "look", IsChecked = _book.VideoLook == "Dark", VerticalAlignment = VerticalAlignment.Center };
            _light.Checked += (_, _) => LookChanged("Light");
            _dark.Checked += (_, _) => LookChanged("Dark");
            look.Children.Add(_light); look.Children.Add(_dark);
            body.Children.Add(look);
            body.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(70, 0, 0, 4), Child = _preview });
            body.Children.Add(new TextBlock { Text = "How a paragraph looks in the video (a paragraph too long for one screen goes on as many as it needs, at sentence ends).", Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(70, 0, 0, 12), TextWrapping = TextWrapping.Wrap });

            var save = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var saveLabel = new TextBlock { Text = "Save as:", Width = 70, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(saveLabel, Dock.Left);
            var browse = new Button { Content = "Browse…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Choose where to save the video" };
            DockPanel.SetDock(browse, Dock.Right);
            _path = new TextBox { Text = defaultPath, Padding = new Thickness(3), ToolTip = "The video file. Its thumbnail and YouTube description are saved beside it" };
            System.Windows.Automation.AutomationProperties.SetName(_path, "Save as");
            browse.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileName(_path.Text), InitialDirectory = SafeDir(_path.Text), Filter = "MP4 video (*.mp4)|*.mp4", Title = "Save the video", OverwritePrompt = false };
                if (dlg.ShowDialog(this) == true) _path.Text = dlg.FileName;
            };
            save.Children.Add(saveLabel); save.Children.Add(browse); save.Children.Add(_path);
            body.Children.Add(save);
            body.Children.Add(new TextBlock
            {
                Text = "Saved beside it: \"… — thumbnail.jpg\" (1280 × 720, the first screen, for YouTube's custom thumbnail) and \"… — YouTube description.txt\" " +
                       "(what the video is, that the narration is a synthesized voice" + (wholeBook ? ", and the chapter times, which YouTube turns into chapters" : "") + ").",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(70, 0, 0, 12)
            });
            body.Children.Add(new TextBlock
            {
                Text = "To put it on YouTube: YouTube Studio ▸ Create ▸ Upload videos; paste the description and choose the thumbnail. " +
                       "A video longer than 15 minutes needs a verified YouTube account, and YouTube takes videos of up to 12 hours" +
                       (_seconds > 12 * 3600 ? " — this one is longer: make it from one part at a time (Export audiobook ▸ \"… only\")." : "."),
                TextWrapping = TextWrapping.Wrap, Foreground = _seconds > 12 * 3600 ? Brushes.DarkRed : Brushes.DimGray, FontSize = 12
            });
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
            Loaded += async (_, _) => await Prepare();
        }

        /// <summary>"3 chapters, with the credits".</summary>
        private static string Chapters(List<TrackPlan> tracks)
        {
            int n = tracks.Count(t => t.Id is not ("opening" or "closing"));
            return $"{n} chapter{(n == 1 ? "" : "s")}" + (tracks.Count > n ? ", with the credits" : "");
        }

        private static string SafeDir(string path) { try { var d = Path.GetDirectoryName(path); return d != null && Directory.Exists(d) ? d : ""; } catch (ArgumentException) { return ""; } }

        private static string Length(double s) => TimeSpan.FromSeconds(s) is var t && t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{Math.Max(1, (int)Math.Round(t.TotalMinutes))} min";

        /// <summary>The time to make it: about a thirtieth of its length (measured 2026-11-09), never less than a few seconds.</summary>
        private static string Making(double s) => s / 30 < 60 ? "a minute or less" : s / 30 < 5400 ? $"{Math.Ceiling(s / 30 / 60)} minutes" : $"{s / 30 / 3600:0.0} hours";

        private VideoLook Look => _dark.IsChecked == true ? VideoLook.Dark : VideoLook.Light;

        private void LookChanged(string name)
        {
            if (_book.VideoLook != name) { _book.VideoLook = name; _w.MarkDirty(); }
            ShowPreview();
        }

        /// <summary>
        /// Works out every track's screens with the window alive and saying how far it has got: a track made before
        /// videos has its sound read once to find its pauses (a moment each; the times are then kept with it).
        /// </summary>
        private async Task Prepare()
        {
            _go.ToolTip = "Getting the video ready…";
            var measure = new VideoPainter(Look, null);
            var parts = new List<VideoPart>();
            TrackPlan? current = null;
            try
            {
                for (int i = 0; i < _tracks.Count; i++)
                {
                    current = _tracks[i];
                    _progressText.Text = _tracks.Count > 1 ? $"Getting the video ready: {current.Label} ({i + 1} of {_tracks.Count})…" : "Getting the video ready…";
                    _progress.Value = i / (double)_tracks.Count;
                    var plan = current;
                    var heard = await Task.Run(() => _w.HeardOf(plan));
                    parts.Add(_w.VideoPartOf(plan, heard, measure));
                }
                _parts = parts;
                _progress.Value = 0;
                _progressText.Text = "";
                _go.IsEnabled = true;
                _go.ToolTip = "Makes the video on this computer from the audiobook track(s) — nothing is sent anywhere or paid for";
                ShowPreview();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                // A damaged track is forgotten, so approving the chapter (or making the audiobook) makes it again.
                if (current != null) try { File.Delete(_w.TrackNotePath(current.Id)); } catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException) { }
                _go.ToolTip = "An audiobook track could not be read (below)";
                _progressText.Text = $"The audiobook track of {current?.Label ?? "a chapter"} could not be read ({ex.Message}). It will be made again the next time you " +
                                     "approve the chapter in Listen mode or make the audiobook; then make the video.";
            }
        }

        private void ShowPreview()
        {
            if (_parts == null) return;
            var screen = _parts.SelectMany(p => p.Screens.Select(s => (p, s))).FirstOrDefault(x => x.s.Kind == ScreenKind.Paragraph);
            var painter = new VideoPainter(Look, null) { Header = screen.p?.Header ?? _book.Title };
            _preview.Source = painter.Draw(screen.s ?? new VideoScreen(0, 1, ScreenKind.Paragraph, "The chapter's first paragraph appears here, as it is read."));
        }

        private async Task Make()
        {
            if (_parts == null) return;
            var path = _path.Text.Trim();
            if (path.Length == 0 || !Path.IsPathRooted(path))
            {
                MessageBox.Show(this, "Choose where to save the video: Browse… next to Save as.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) path += ".mp4";
            string thumb, description;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path));
                thumb = stem + " — thumbnail.jpg";
                description = stem + " — YouTube description.txt";
                if (File.Exists(path))
                {
                    if (MessageBox.Show(this, $"\"{Path.GetFileName(path)}\" is there already. Replace it, with its thumbnail and YouTube description?", Title,
                                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    try { using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                    catch (IOException)
                    {
                        MessageBox.Show(this, $"\"{Path.GetFileName(path)}\" is open in another program (a video player?). Close it, then Make the video again.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { MessageBox.Show(this, "The video cannot be saved there: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            _cts = new CancellationTokenSource();
            _go.IsEnabled = false; _stop.IsEnabled = true; _light.IsEnabled = _dark.IsEnabled = false;
            _progress.Value = 0;
            var watch = Stopwatch.StartNew();
            _progressText.Text = "Making the video…";
            try
            {
                var marks = new List<ChapterMark>();
                double at = 0;
                for (int i = 0; i < _tracks.Count; i++) { marks.Add(new ChapterMark(_tracks[i].Label, TimeSpan.FromSeconds(at))); at += _parts[i].Seconds; }
                var saved = await VideoMaker.Make(path, _parts, Look, AudiobookExport.CoverOf(_book), thumb, p => Dispatcher.BeginInvoke(() =>
                {
                    _progress.Value = p;
                    var left = p > 0.02 ? TimeSpan.FromSeconds(watch.Elapsed.TotalSeconds * (1 - p) / p) : (TimeSpan?)null;
                    _progressText.Text = $"Making the video: {p:P0}" + (left is { } l ? $" — about {(l.TotalMinutes < 1 ? "a minute" : $"{Math.Ceiling(l.TotalMinutes)} minutes")} left" : "") + ".";
                }), _cts.Token);
                var what = _wholeBook ? _what : $"{_book.Title} — {_tracks[0].Label}";
                await File.WriteAllTextAsync(description, VideoScreens.YouTubeDescription(what, _book.Author, $"Google {_book.NarratorVoice?.Label ?? "voice"}",
                                                                                          _wholeBook ? marks : new List<ChapterMark>(), TimeSpan.FromSeconds(_seconds)));
                _progress.Value = 1;
                _progressText.Text = $"The video is made: {Path.GetFileName(saved)} ({new FileInfo(saved).Length / 1048576.0:0.0} MB, {Length(_seconds)}), with its thumbnail and YouTube description beside it." +
                                     (saved != path ? $" (\"{Path.GetFileName(path)}\" was in use, so it was saved under this name.)" : "");
                _w.UpdateStatus("Video made — " + saved);
            }
            catch (OperationCanceledException) { _progressText.Text = "Stopped. No video was saved."; _progress.Value = 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                _progressText.Text = "The video could not be made: " + ex.Message;
                MessageBox.Show(this, "The video could not be made: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _cts.Dispose(); _cts = null;
                _go.IsEnabled = true; _stop.IsEnabled = false; _light.IsEnabled = _dark.IsEnabled = true;
            }
        }

        private void OpenFolder()
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(_path.Text.Trim()))!;
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException) { }
        }
    }
}
