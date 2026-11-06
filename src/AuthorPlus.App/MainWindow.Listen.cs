using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using Eaglin.AiManager;
using Eaglin.AiManager.Wpf;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace AuthorPlus.App;

/// <summary>
/// Listen mode (Ron, 2026-11-06; plan phase 9): the chapter read aloud by Google voices through AI
/// Manager — the narrator's voice for narration, each character's for their lines. Every line of
/// dialogue is coloured by its speaker (the AI marks them, the author confirms or changes them);
/// Play reads from the chosen paragraph with the piece being read highlighted. Each spoken piece is
/// kept in the book's <c>audio\cache</c> folder (never versioned or exported), so hearing it again is free.
/// </summary>
public partial class MainWindow
{
    private Item? _speakers;                                   // Listen: the chapter's speakers item, if any
    private List<(Paragraph Paragraph, string Text, List<(Run Run, int Start)> Runs)> _listenParas = new();
    private IReadOnlyList<QuoteLine> _quoteLines = Array.Empty<QuoteLine>();
    private QuoteLine? _panelQuote;                             // Listen: the line open in the panel
    private int _listenFrom;                                    // Listen: the paragraph Play starts from
    private MediaPlayer? _player;
    private CancellationTokenSource? _playCts;
    private string _playStatus = "";
    private double _spentThisSession;
    private bool _costAgreed;                                   // Listen: the author agreed to what playing costs, this session
    private TextBlock? _playStatusText;                         // Listen: the status line, updated in place while playing

    /// <summary>A spoken piece that could not be played (not a problem saving it).</summary>
    private sealed class PlaybackException(string message) : Exception(message);

    /// <summary>The paragraphs that are read: with words in them (not blank lines or scene breaks).</summary>
    private List<int> ReadParagraphIndexes() =>
        Enumerable.Range(0, _listenParas.Count).Where(i => _listenParas[i].Text.Any(char.IsLetterOrDigit)).ToList();

    private string ParagraphWord(int paragraph)
    {
        var read = ReadParagraphIndexes();
        int n = read.IndexOf(paragraph);
        return n < 0 ? "" : $"paragraph {n + 1} of {read.Count}";
    }

    private static readonly Brush NarratorWash = Rgb(0xD8, 0xDC, 0xE2);

    private Item? SpeakersOf(Chapter ch) => _book!.ItemsOf(ch.Id).FirstOrDefault(i => i.Kind == ItemKind.ChapterSpeakers);

    private Item SpeakersFor(Chapter ch)
    {
        if (SpeakersOf(ch) is { } it) return it;
        var made = new Item { OwnerId = ch.Id, Kind = ItemKind.ChapterSpeakers, Title = "Who speaks" };
        _book!.Items.Add(made);
        return made;
    }

    private void ReadListenParagraphs()
    {
        _listenParas = _rtb == null ? new() : AllParagraphs(_rtb.Document.Blocks).Select(p => { var (t, r) = RunsOf(p); return (p, t, r); }).ToList();
        _quoteLines = Dialogue.Lines(_listenParas.Select(x => x.Text).ToList());
    }

    /// <summary>Listen mode's marks: every line of dialogue washed in its speaker's colour; dotted when no one is known yet.</summary>
    private void ShowListenMarks(Chapter ch)
    {
        ReadListenParagraphs();
        _speakers = SpeakersOf(ch);
        _castOrder.Clear();
        _nameMarks = new List<NameMark>();
        foreach (var line in _quoteLines)
        {
            var runs = _listenParas[line.Paragraph].Runs;
            var start = PointerAt(runs, line.Start, end: false);
            var end = PointerAt(runs, line.Start + line.Length, end: true);
            if (start == null || end == null) continue;
            var who = Dialogue.SpeakerOf(_speakers, line);
            var c = who?.CharacterId is { } id ? _book!.Characters.FirstOrDefault(x => x.Id == id) : null;
            if (c != null && !_castOrder.ContainsKey(c.Id)) _castOrder[c.Id] = _castOrder.Count;
            Brush? fill = c != null ? Wash(c) : who != null && who.SpeakerName == "Narrator" ? NarratorWash : null;
            var tip = c != null ? $"{c.Name} says this — click to change"
                    : who?.SpeakerName == "Narrator" ? "The narrator reads this — click to change"
                    : who?.SpeakerName is { } named ? $"The AI says {named} says this — not a character yet; click to choose"
                    : "Who says this is not known yet — click to choose";
            _nameMarks.Add(new NameMark(start, end, fill, line, tip));
        }
        _overlay?.ShowNames(_nameMarks, _panelQuote);
        _overlay?.SetStart(_listenFrom >= 0 && _listenFrom < _listenParas.Count ? _listenParas[_listenFrom].Paragraph : null);
    }

    private (TextPointer Start, TextPointer End)? PointersOf(int paragraph, int start, int length)
    {
        if (paragraph < 0 || paragraph >= _listenParas.Count) return null;
        var runs = _listenParas[paragraph].Runs;
        var a = PointerAt(runs, start, end: false);
        var b = PointerAt(runs, start + length, end: true);
        return a != null && b != null ? (a, b) : null;
    }

    // ── The panel ─────────────────────────────────────────────────────────────

    private UIElement ListenPanel(Chapter ch)
    {
        var p = PanelStack();
        p.Children.Add(Heading("🔊  Listen"));
        p.Children.Add(Hint("The chapter read aloud: the narrator's voice for the story, each character's voice for what they say. Lines of dialogue are " +
                            "coloured by who says them — click one to change it. Click a paragraph to start there (▶ in the margin), then Play. Space plays and stops; Esc stops."));

        if (!_ai.CanSpeak)
        {
            p.Children.Add(Card("Connect Google voices", Body("Listening uses Google's text-to-speech voices. Add a Google text-to-speech key in AI Settings — " +
                                                               "it shows you, step by step, how to get one. Google gives a number of characters free each month."),
                PanelButton("AI Settings…", "Add a Google text-to-speech key (or a service-account file)", () => { AiSettings_Click(this, new RoutedEventArgs()); ShowSidePanel(); }, primary: true)));
        }

        // Play
        var play = new StackPanel();
        var narrator = _book!.NarratorVoice;
        _playStatusText = Body(_playStatus.Length > 0 ? _playStatus
            : $"Starts at {(ParagraphWord(_listenFrom) is { Length: > 0 } w ? w : "the first paragraph")} (▶). " + CostLine(ch).Text, muted: _playStatus.Length == 0);
        play.Children.Add(_playStatusText);
        var playing = _playCts != null;
        var buttons = new List<Button>();
        if (playing) buttons.Add(PanelButton("■  Stop", "Stops reading", StopListening, primary: true));
        else
        {
            var go = PanelButton("▶  Play", "Reads the chapter aloud from the paragraph marked ▶ (Space)", () => PlayFrom(ch, _listenFrom), primary: true);
            var top = PanelButton("▶  From the start", "Reads the chapter aloud from the first paragraph", () => { _listenFrom = 0; PlayFrom(ch, 0); });
            if (!_ai.CanSpeak || narrator == null)
            {
                foreach (var b in new[] { go, top }) { b.IsEnabled = false; ToolTipService.SetShowOnDisabled(b, true); b.ToolTip = !_ai.CanSpeak ? "Connect Google voices first (above)" : "Choose the narrator's voice first (below)"; }
            }
            buttons.Add(go); buttons.Add(top);
        }
        p.Children.Add(Card("Play", play, buttons.ToArray()));
        if (_ai.CanSpeak && narrator != null) p.Children.Add(AudiobookCard(ch, narrator));

        // Voices
        var voices = new StackPanel();
        voices.Children.Add(VoiceRow("Narrator", null, narrator, "Reads everything that is not a character's speech", v => { _book.NarratorVoice = v; MarkDirty(); ShowSidePanel(); }));
        var speaking = SpeakingCast();
        foreach (var c in speaking)
        {
            var who = c;
            voices.Children.Add(VoiceRow(c.Name, Solid(c), c.Voice, $"Reads what {c.Name} says", v => { who.Voice = v; MarkDirty(); ShowSidePanel(); }));
        }
        if (speaking.Count == 0)
            voices.Children.Add(Body(_quoteLines.Count == 0 ? "No one speaks in this chapter — the narrator reads it all."
                                     : "No character has a line in this chapter yet — find who speaks (below), or click a line in the text.", muted: true));
        p.Children.Add(Card("Voices", voices));

        // Who speaks
        var known = _quoteLines.Count(l => Dialogue.SpeakerOf(_speakers, l) is { } s && (s.CharacterId != null || s.SpeakerName == "Narrator"));
        var unknown = _quoteLines.Count - known;
        var others = _quoteLines.Select(l => Dialogue.SpeakerOf(_speakers, l)).Where(s => s?.CharacterId == null && s?.SpeakerName is { } n && n != "Narrator")
                                .Select(s => s!.SpeakerName!).Distinct().ToList();
        var whoBody = new StackPanel();
        whoBody.Children.Add(Body(_quoteLines.Count == 0 ? "No lines of dialogue were found, so the narrator reads it all. Only double quotation marks (\"…\" or “…”) are recognized as dialogue for now."
            : $"{_quoteLines.Count} line{(_quoteLines.Count == 1 ? "" : "s")} of dialogue: {known} with a speaker, {unknown} not known yet (dotted underline; the narrator reads them)."));
        if (others.Count > 0)
            whoBody.Children.Add(Body($"The AI named people who are not characters yet: {string.Join(", ", others)}. Click one of their lines to add them or choose who it is.", muted: true));
        var whoButtons = new List<Button>();
        if (_quoteLines.Count > 0)
            whoButtons.Add(AiButton(_speakers == null ? "Find who speaks (AI)…" : "Find who speaks again (AI)…",
                "The AI reads the chapter and names the speaker of each line of dialogue. Lines you have chosen yourself are kept.", () => FindSpeakers(ch), primary: _speakers == null));
        if (unknown > 0)
            whoButtons.Add(PanelButton("Next not known ›", "Opens the next line whose speaker is not known", () => OpenQuote(_quoteLines.FirstOrDefault(l => !Known(l)))));
        p.Children.Add(Card("Who speaks", whoBody, whoButtons.ToArray()));
        return Scroll(p);
    }

    // ── This chapter in the audiobook (Ron, 2026-11-06: "the author listens to a chapter and then approves that chapter") ─

    private CancellationTokenSource? _approveCts;
    private string _approveStatus = "";
    private TextBlock? _approveText;

    /// <summary>
    /// The chapter's audiobook track: approve it once it sounds right (the track is made now, at the audiobook's
    /// quality, and the export uses it as it is), hear the made track, save its MP3, or withdraw the approval.
    /// </summary>
    private Border AudiobookCard(Chapter ch, VoiceChoice narrator)
    {
        var plan = ChapterPlan(ch, narrator);
        var note = TrackNoteOf(plan.Id);
        var why = WhyRemake(plan);
        bool fresh = why == null;
        var (chars, cost) = TrackCost(plan);
        var price = cost is { } usd ? $"about ${usd:0.00}" : "a price not known for one of the voices";
        var body = new StackPanel();
        string text =
            _approveCts != null ? _approveStatus
            : fresh && note!.Approved ? $"Approved for the audiobook{(note.ApprovedUtc is { } d ? $" on {d.ToLocalTime():d MMM yyyy}" : "")} — {TimeSpan.FromSeconds(note.Seconds):m\\:ss} of audio. The audiobook export uses this track as it is."
            : fresh ? $"Its audiobook track is made ({TimeSpan.FromSeconds(note!.Seconds):m\\:ss}) but not approved yet. Hear it, then approve it."
            : note != null ? $"Its audiobook track is out of date: {why}. When the chapter sounds right, approve it again — the new track costs {price} ({chars:N0} characters)."
            : $"Not in the audiobook yet. When the chapter sounds right, approve it: its track is made now at the audiobook's quality (192 kbps, steady loudness, " +
              $"its title read first) — {chars:N0} characters, {price} — and the audiobook export uses it as it is.";
        _approveText = Body(text, muted: !fresh && _approveCts == null);
        body.Children.Add(_approveText);

        var buttons = new List<Button>();
        if (_approveCts != null)
            buttons.Add(PanelButton("Stop", "Stops making the track; the pieces already made are kept and not paid for again", () => _approveCts?.Cancel(), primary: true));
        else
        {
            if (!(fresh && note!.Approved))
                buttons.Add(AiButton(fresh ? "✓ Approve for the audiobook" : note?.Approved == true ? "✓ Approve again…" : "✓ Approve for the audiobook…",
                    fresh ? "Marks the made track as approved; the export uses it as it is"
                          : "Makes this chapter's audiobook track now, as you have been hearing it (voices, speakers, pronunciations), and marks it approved",
                    () => ApproveChapter(ch, plan, alreadyMade: fresh), primary: true));
            if (fresh)
            {
                buttons.Add(PanelButton("▶ Hear the track", "Plays the audiobook track itself — the chapter as the audiobook has it", () => PlayTrack(ch, plan)));
                buttons.Add(PanelButton("Save the MP3…", "Saves this chapter's audiobook MP3 where you choose — to send to a reader, or to upload one chapter", () => SaveTrack(ch, plan)));
                if (note!.Approved)
                    buttons.Add(PanelButton("Withdraw approval", "The track stays made, but the export no longer counts it as approved", () =>
                    {
                        SaveTrackNote(plan, approved: false); ShowSidePanel(keepScroll: true); UpdateStatus($"\"{ch.Title}\" is no longer approved for the audiobook.");
                    }));
            }
        }
        return Card("Audiobook", body, buttons.ToArray());
    }

    private async void ApproveChapter(Chapter ch, TrackPlan plan, bool alreadyMade)
    {
        // The chapter as it is now (a speaker or a word may have changed since the panel was drawn).
        if (_book?.NarratorVoice is { } narrator) { plan = ChapterPlan(ch, narrator); alreadyMade = WhyRemake(plan) == null; }
        if (alreadyMade)
        {
            SaveTrackNote(plan, approved: true);
            ShowSidePanel(keepScroll: true);
            UpdateStatus($"\"{ch.Title}\" is approved for the audiobook.");
            return;
        }
        var (chars, cost) = TrackCost(plan);
        if ((cost == null || cost > 0.25) &&
            MessageBox.Show(this, $"Make the audiobook track for \"{ch.Title}\": about {chars:N0} characters, " +
                                  (cost is { } usd ? $"about ${usd:0.00}" : "an amount not known for one of the voices") +
                                  " at Google's price (its free characters each month come first)?\n\nYou can stop at any time; nothing paid for is lost.",
                            "Approve for the audiobook", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        StopListening();
        CommitCurrent();
        var cts = new CancellationTokenSource();
        _approveCts = cts;
        _approveStatus = "Making the audiobook track: getting the pieces from Google…";
        ShowSidePanel(keepScroll: true);
        var spent = new Spent();
        try
        {
            await MakeTrack(plan, cts.Token, spent, (i, n) =>
            {
                _approveStatus = $"Making the audiobook track: piece {i} of {n}…";
                if (_approveText != null) _approveText.Text = _approveStatus;
            });
            _approveStatus = "Making the audiobook track: putting it together…";
            if (_approveText != null) _approveText.Text = _approveStatus;
            SaveTrackNote(plan, approved: true);
            UpdateStatus($"\"{ch.Title}\" is approved for the audiobook" + (spent.Usd > 0 ? $" (about ${spent.Usd:0.00} of speech made)." : "."));
        }
        catch (OperationCanceledException) { UpdateStatus("Stopped. The pieces already made are kept — approving again carries on from there."); }
        catch (AiException ex) { MessageBox.Show(this, ex.Message, "Approve for the audiobook", MessageBoxButton.OK, MessageBoxImage.Warning); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, "The track could not be made: " + ex.Message, "Approve for the audiobook", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally
        {
            if (ReferenceEquals(_approveCts, cts)) _approveCts = null;
            cts.Dispose();
            if (_mode == EditorMode.Listen && _side != null && ReferenceEquals(_current, ch)) ShowSidePanel(keepScroll: true);
        }
    }

    private async void PlayTrack(Chapter ch, TrackPlan plan)
    {
        StopListening();
        var cts = new CancellationTokenSource();
        _playCts = cts;
        _playStatus = $"Playing the audiobook track of \"{ch.Title}\" — Stop (or Esc) to stop.";
        ShowSidePanel(keepScroll: true);
        try { await PlayFile(TrackMp3(plan.Id), cts.Token); }
        catch (OperationCanceledException) { }
        catch (PlaybackException ex) { MessageBox.Show(this, ex.Message, "Listen", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally
        {
            if (ReferenceEquals(_playCts, cts)) { _playCts = null; _playStatus = ""; if (_mode == EditorMode.Listen && _side != null) ShowSidePanel(keepScroll: true); }
            cts.Dispose();
        }
    }

    private void SaveTrack(Chapter ch, TrackPlan plan)
    {
        var name = $"{_book!.ChapterNumber(ch):00} {ch.Title}";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = name + ".mp3", Filter = "MP3 audio (*.mp3)|*.mp3", Title = "Save this chapter's audiobook MP3" };
        if (dlg.ShowDialog(this) != true) return;
        try { File.Copy(TrackMp3(plan.Id), dlg.FileName, overwrite: true); UpdateStatus("Saved: " + dlg.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, "It could not be saved there: " + ex.Message, "Save the MP3", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private bool Known(QuoteLine l) => Dialogue.SpeakerOf(_speakers, l) is { } s && (s.CharacterId != null || s.SpeakerName == "Narrator");

    /// <summary>The characters with a line in this chapter, in the order they first speak.</summary>
    private List<Character> SpeakingCast() =>
        _quoteLines.Select(l => Dialogue.SpeakerOf(_speakers, l)?.CharacterId).OfType<Guid>().Distinct()
                   .Select(id => _book!.Characters.FirstOrDefault(c => c.Id == id)).OfType<Character>().ToList();

    /// <summary>What playing from the start paragraph will cost: characters not heard before, and their price (null when a voice's price is not known).</summary>
    private (string Text, int Chars, double? Cost) CostLine(Chapter ch)
    {
        if (_book!.NarratorVoice is not { } narrator) return ("Choose the narrator's voice below.", 0, 0);
        var plan = Dialogue.Plan(_listenParas.Select(x => x.Text).ToList(), _speakers);
        int chars = 0; double? cost = 0;
        foreach (var part in plan.Where(x => x.Paragraph >= _listenFrom))
        {
            var v = VoiceFor(part, narrator);
            var spoken = Spoken(part.Text, v);
            if (File.Exists(CachePath(v, spoken))) continue;
            chars += spoken.Text.Length;
            var c = _ai.EstimateSpeech(v.Name, spoken.Text.Length);
            cost = c == null || cost == null ? null : cost + c;
        }
        if (chars == 0) return ("All of it has been heard before — playing it again costs nothing.", 0, 0);
        var price = cost is { } usd ? $"≈ ${usd:0.00} at Google's price" : "— the price of one of the voices is not known";
        return ($"About {chars:N0} characters not heard before {price} (Google's free monthly characters come first). What you have heard is kept and not paid for again.", chars, cost);
    }

    private static readonly double[] Speeds = { 0.8, 0.9, 1.0, 1.1, 1.2 };

    /// <summary>A voice row: the name, its colour and its voice on one line; Choose…, the pace and "Use the narrator's" under it.</summary>
    private UIElement VoiceRow(string who, Brush? colour, VoiceChoice? voice, string tip, Action<VoiceChoice?> set)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        var line = new DockPanel();
        if (colour != null) line.Children.Add(Swatch(colour));
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
        text.Inlines.Add(new Run(who) { FontWeight = FontWeights.SemiBold });
        text.Inlines.Add(new Run("  " + (voice != null ? (voice.Label.Length > 0 ? voice.Label : voice.Name) : who == "Narrator" ? "not chosen yet" : "the narrator's voice"))
                         { Foreground = Brushes.DimGray });
        line.Children.Add(text);
        stack.Children.Add(line);

        var controls = new WrapPanel { Margin = new Thickness(colour != null ? 18 : 0, 0, 0, 0) };
        var choose = PanelButton("Choose…", $"Choose the voice. {tip}.", () =>
        {
            if (!_ai.CanSpeak) { AiSettings_Click(this, new RoutedEventArgs()); if (!_ai.CanSpeak) return; }
            var dlg = new VoiceChooserWindow(_ai, voice?.Name, _book!.VoiceLanguage, SampleFor(who)) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SelectedVoice is { } v)
                set(new VoiceChoice { Name = v.Name, Label = v.Label, SpeakingRate = voice?.SpeakingRate ?? 1.0 });
        });
        controls.Children.Add(choose);
        if (voice != null)
        {
            var speed = new ComboBox { ItemsSource = Speeds.Select(s => s == 1.0 ? "normal pace" : $"{s:0.0}×").ToList(), Margin = new Thickness(0, 6, 6, 0), MinWidth = 104,
                                       SelectedIndex = Math.Max(0, Array.IndexOf(Speeds, Speeds.MinBy(s => Math.Abs(s - voice.SpeakingRate)))),
                                       VerticalAlignment = VerticalAlignment.Center,
                                       ToolTip = "How fast this voice reads. Pieces already heard at another pace are made again (and paid for) when played." };
            System.Windows.Automation.AutomationProperties.SetName(speed, $"{who}'s pace");
            speed.SelectionChanged += (_, _) => set(new VoiceChoice { Name = voice.Name, Label = voice.Label, SpeakingRate = Speeds[speed.SelectedIndex] });
            controls.Children.Add(speed);
        }
        if (voice != null && who != "Narrator")
            controls.Children.Add(PanelButton("Use the narrator's", $"Read {who}'s lines in the narrator's voice instead", () => set(null)));
        stack.Children.Add(controls);
        return stack;
    }

    private string SampleFor(string who) =>
        who == "Narrator" ? $"The lamp at the end of the chapter had burned every night for forty-one years. This is how {_book!.Title} sounds read aloud."
                          : _quoteLines.Select(l => (l, Dialogue.SpeakerOf(_speakers, l))).FirstOrDefault(x => x.Item2?.CharacterId is { } id &&
                                _book!.Characters.FirstOrDefault(c => c.Id == id)?.Name == who).l?.Key ?? $"My name is {who}, and this is how I sound.";

    // ── One line of dialogue ──────────────────────────────────────────────────

    private void OpenQuote(QuoteLine? line)
    {
        if (line == null) { UpdateStatus("Every line of dialogue has a speaker."); return; }
        _panelQuote = line;
        RefreshMarks();
        ShowSidePanel();
        if (PointersOf(line.Paragraph, line.Start, line.Length) is { } at) ScrollTo(at.Start);
    }

    private UIElement QuotePanel(Chapter ch, QuoteLine line)
    {
        var p = PanelStack();
        p.Children.Add(BackLink("Listen — the whole chapter", () => { _panelQuote = null; RefreshMarks(); ShowSidePanel(); }));
        if (_playCts != null)
            p.Children.Add(Card("Playing", Body("Changes here are heard the next time you press Play."), PanelButton("■  Stop", "Stops reading (Esc)", () => { StopListening(); ShowSidePanel(); }, primary: true)));
        int index = _quoteLines.ToList().FindIndex(l => l.Paragraph == line.Paragraph && l.Start == line.Start);
        var nav = new DockPanel();
        var arrows = new StackPanel { Orientation = Orientation.Horizontal };
        var prev = new Button { Content = "‹ Previous", Padding = new Thickness(8, 2, 8, 2), IsEnabled = index > 0, ToolTip = "The line of dialogue before" };
        var next = new Button { Content = "Next ›", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), IsEnabled = index < _quoteLines.Count - 1, ToolTip = "The next line of dialogue" };
        prev.Click += (_, _) => OpenQuote(_quoteLines[index - 1]);
        next.Click += (_, _) => OpenQuote(_quoteLines[index + 1]);
        arrows.Children.Add(prev); arrows.Children.Add(next);
        DockPanel.SetDock(arrows, Dock.Right);
        nav.Children.Add(arrows);
        nav.Children.Add(Heading($"Line {index + 1} of {_quoteLines.Count}: who says it?"));
        p.Children.Add(nav);
        p.Children.Add(new TextBox
        {
            Text = _listenParas[line.Paragraph].Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Georgia"),
            FontSize = 13, Padding = new Thickness(6), MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 4),
            ToolTip = "The paragraph the line is in"
        });
        p.Children.Add(Context("The line:", line.Key));

        var current = Dialogue.SpeakerOf(_speakers, line);
        if (current?.CharacterId == null && current?.SpeakerName is { } named && named != "Narrator")
            p.Children.Add(Body($"The AI says {named} says this; there is no character of that name.", muted: true));

        var choices = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        bool wasKnown = Known(line);
        void Choose(Guid? id, string? name)
        {
            Dialogue.SetSpeaker(SpeakersFor(ch), line, id, name, byAuthor: true);
            _speakers = SpeakersOf(ch);
            MarkDirty();
            // A line that had no speaker: on to the next such line. A correction: stay here (Next › is there).
            var after = wasKnown ? null : _quoteLines.Skip(index + 1).FirstOrDefault(l => !Known(l));
            RefreshMarks();
            if (after != null) OpenQuote(after); else ShowSidePanel();
        }
        var inChapter = SpeakingCast().Concat(_book!.Characters.Where(c => ch.CharacterIds.Contains(c.Id))).Distinct().ToList();
        var rest = _book.Characters.Except(inChapter).OrderBy(c => c.Name).ToList();
        foreach (var c in inChapter.Concat(rest))
        {
            var who = c;
            if (rest.Count > 0 && ReferenceEquals(c, rest[0]))
                choices.Children.Add(new TextBlock { Text = inChapter.Count > 0 ? "Others in the book" : "The book's characters", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 8, 0, 2) });
            var b = new RadioButton { Content = c.Name, IsChecked = current?.CharacterId == c.Id, GroupName = "Speaker", Margin = new Thickness(0, 3, 0, 3),
                                      ToolTip = $"{c.Name} says this line" + (c.Voice != null ? $" (their voice: {c.Voice.Label})" : " (in the narrator's voice until they have one)") };
            b.Checked += (_, _) => { if (current?.CharacterId != who.Id) Choose(who.Id, null); };
            choices.Children.Add(b);
        }
        var narr = new RadioButton { Content = "The narrator — not speech, or read plainly", IsChecked = current?.SpeakerName == "Narrator", GroupName = "Speaker", Margin = new Thickness(0, 6, 0, 3),
                                     ToolTip = "A sign, a title, a thought or words in quotation marks that no one says aloud: the narrator reads it" };
        narr.Checked += (_, _) => { if (current?.SpeakerName != "Narrator") Choose(null, "Narrator"); };
        choices.Children.Add(narr);
        p.Children.Add(Card("Who says it?", choices));

        var add = new StackPanel();
        var nameBox = new TextBox { Text = current?.CharacterId == null && current?.SpeakerName is { } n2 && n2 != "Narrator" ? n2 : "", Padding = new Thickness(4),
                                    ToolTip = "The name of someone who is not a character yet" };
        System.Windows.Automation.AutomationProperties.SetName(nameBox, "New character's name");
        add.Children.Add(Body("Someone who is not a character yet:", muted: true));
        add.Children.Add(nameBox);
        p.Children.Add(Card("Someone new", add, PanelButton("Add them and choose them", "Adds a character of this name to the book, and says they speak this line", () =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0) { nameBox.Focus(); return; }
            var asked = current?.CharacterId == null ? current?.SpeakerName : null;
            var c = Dialogue.Match(name, _book.Characters) ?? new Character { Name = name };
            if (!_book.Characters.Contains(c)) { _book.Characters.Add(c); RefreshTreeTexts(); BuildTree(select: ch); }
            // Every other line the AI gave to the same name (and the author has not set) is theirs too.
            int more = 0;
            var item = SpeakersFor(ch);
            foreach (var l in _quoteLines)
                if (Dialogue.SpeakerOf(item, l) is { CharacterId: null, ByAuthor: false } s && s.SpeakerName != null &&
                    (s.SpeakerName.Equals(name, StringComparison.OrdinalIgnoreCase) || (asked != null && s.SpeakerName.Equals(asked, StringComparison.OrdinalIgnoreCase))))
                { Dialogue.SetSpeaker(item, l, c.Id, null, byAuthor: false); more++; }
            Choose(c.Id, null);
            UpdateStatus($"{c.Name} is a character now and says {more + (more > 0 && Dialogue.SpeakerOf(item, line)?.CharacterId == c.Id ? 0 : 1)} line(s) in this chapter.");
        })));
        return Scroll(p);
    }

    // ── Finding who speaks ────────────────────────────────────────────────────

    private void FindSpeakers(Chapter ch)
    {
        if (_book == null) return;
        ReadListenParagraphs();
        if (_quoteLines.Count == 0) { MessageBox.Show(this, "This chapter has no lines of dialogue (nothing in quotation marks).", "Who speaks", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var cast = string.Join("\n", _book.Characters.Select(c =>
        {
            var also = string.Join(", ", Bullets.Parse(c.Aliases).Select(b => b.Text));
            var role = string.Join("; ", Bullets.Parse(c.Role).Select(b => b.Text));
            return $"- {c.Name}{(also.Length > 0 ? $" (also called {also})" : "")}{(role.Length > 0 ? $" — {role}" : "")}";
        }));
        var values = new
        {
            book = _book.Title, chapter = $"{_book.ChapterNumber(ch)}. {ch.Title}", count = _quoteLines.Count,
            cast = cast.Length > 0 ? cast : "(no characters listed yet)",
            text = Dialogue.Marked(_listenParas.Select(x => x.Text).ToList(), _quoteLines)
        };
        string? reply = null;
        var dlg = new AiRunWindow(_ai, _book, ch.Id, ItemKind.ChapterSpeakers, $"Who speaks in \"{ch.Title}\"", "Who speaks", AiPrompts.DialogueSpeakers,
            $"The AI names the speaker of each of the {_quoteLines.Count} lines of dialogue. Lines you chose yourself are kept. The prompt is \"dialogue-speakers\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.DialogueSpeakers).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: see them in the chapter →", handleResponse: r => reply = r.Text) { Owner = this };
        dlg.ShowDialog();
        if (reply == null) return;

        var answers = Dialogue.ParseSpeakers(reply);
        var item = SpeakersFor(ch);
        int named = 0, kept = 0;
        for (int n = 0; n < _quoteLines.Count; n++)
        {
            if (!answers.TryGetValue(n + 1, out var name) || Dialogue.IsUnknown(name)) continue;
            if (Dialogue.SpeakerOf(item, _quoteLines[n])?.ByAuthor == true) { kept++; continue; }
            if (Dialogue.IsNarrator(name)) Dialogue.SetSpeaker(item, _quoteLines[n], null, "Narrator", false);
            else if (Dialogue.Match(name, _book.Characters) is { } c) Dialogue.SetSpeaker(item, _quoteLines[n], c.Id, null, false);
            else Dialogue.SetSpeaker(item, _quoteLines[n], null, name, false);
            named++;
        }
        MarkDirty();
        _speakers = item;
        RefreshMarks();
        ShowSidePanel();
        UpdateStatus($"Speakers marked for {named} of {_quoteLines.Count} lines" + (kept > 0 ? $"; {kept} you chose were kept" : "") + ". Click any line to change who says it.");
    }

    // ── Playing ───────────────────────────────────────────────────────────────

    private VoiceChoice VoiceFor(ReadPart part, VoiceChoice narrator) =>
        part.IsQuote && part.CharacterId is { } id && _book!.Characters.FirstOrDefault(c => c.Id == id)?.Voice is { } v ? v : narrator;

    private string CachePath(VoiceChoice v, SpokenText spoken) =>
        Path.Combine(_book!.FolderPath!, "audio", "cache", Dialogue.AudioKey(v.Name, v.SpeakingRate, spoken.Key) + ".mp3");

    /// <summary>The spoken piece: from the cache, or made by Google and kept.</summary>
    private async Task<string> AudioFor(ReadPart part, VoiceChoice v, CancellationToken ct)
    {
        var spoken = Spoken(part.Text, v);
        var path = CachePath(v, spoken);
        if (File.Exists(path)) return path;
        var speech = await _ai.SpeakAsync(SpeechRequest(spoken, v, storeQuality: false, "listen-chapter"), null, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, speech.Audio, ct);
        File.Move(tmp, path, overwrite: true);
        _spentThisSession += speech.EstimatedCostUsd ?? 0;
        return path;
    }

    private async void PlayFrom(Chapter ch, int paragraph)
    {
        StopListening();
        if (_book?.NarratorVoice is not { } narrator) { UpdateStatus("Choose the narrator's voice first."); return; }
        ReadListenParagraphs();
        _speakers = SpeakersOf(ch);
        var plan = Dialogue.Plan(_listenParas.Select(x => x.Text).ToList(), _speakers).Where(x => x.Paragraph >= paragraph).ToList();
        if (plan.Count == 0) { UpdateStatus("Nothing to read from there."); return; }
        // What it costs, before anything is paid for: asked once a session when it is more than a few cents or not known.
        var (_, chars, cost) = CostLine(ch);
        if (!_costAgreed && chars > 0 && (cost == null || cost > 0.25))
        {
            var what = cost is { } usd ? $"about ${usd:0.00}" : "an amount that is not known for one of the voices";
            if (MessageBox.Show(this, $"Reading from here sends about {chars:N0} characters to Google, {what} at its price (its free monthly characters come first). " +
                                      "Pieces you have heard are kept and not paid for again.\n\nPlay it?", "Listen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _costAgreed = true;
        }
        var cts = new CancellationTokenSource();
        _playCts = cts;
        _playStatus = "Getting the voices from Google…";
        ShowSidePanel(keepScroll: true);
        try
        {
            Task<string>? nextFile = null;
            for (int i = 0; i < plan.Count; i++)
            {
                var part = plan[i];
                var v = VoiceFor(part, narrator);
                var file = await (nextFile ?? AudioFor(part, v, cts.Token));
                file = await FreshIfBroken(file, part, v, cts.Token);
                nextFile = i + 1 < plan.Count ? AudioFor(plan[i + 1], VoiceFor(plan[i + 1], narrator), cts.Token) : null;   // the next piece is fetched while this one plays
                if (cts.IsCancellationRequested || !ReferenceEquals(_current, ch)) break;
                var speaker = part.CharacterId is { } id ? _book.Characters.FirstOrDefault(c => c.Id == id)?.Name : null;
                _playStatus = $"Reading {ParagraphWord(part.Paragraph)}" + (part.IsQuote ? $" — {speaker ?? "a line no one is known to say"}" : " — the narrator") +
                              (_spentThisSession > 0 ? $". Spent this session: about ${_spentThisSession:0.00}." : ".");
                _listenFrom = part.Paragraph;
                if (PointersOf(part.Paragraph, part.Start, part.Length) is { } at)
                {
                    _overlay?.SetSpeaking(at.Start, at.End);
                    ScrollTo(at.Start);
                }
                if (_playStatusText != null) _playStatusText.Text = _playStatus;      // only the line changes: Stop stays where it is
                await PlayFile(file, cts.Token);
            }
            if (!cts.IsCancellationRequested) { _playStatus = ""; _listenFrom = 0; UpdateStatus("Finished reading the chapter."); }
        }
        catch (OperationCanceledException) { }
        catch (AiException ex)
        {
            _playStatus = "";
            MessageBox.Show(this, ex.Message, "Listen", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (ex.Kind == AiErrorKind.NoKey) AiSettings_Click(this, new RoutedEventArgs());
        }
        catch (PlaybackException ex)
        {
            _playStatus = "";
            MessageBox.Show(this, ex.Message, "Listen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (IOException ex)
        {
            _playStatus = "";
            MessageBox.Show(this, "The spoken audio could not be saved in the book folder: " + ex.Message, "Listen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (ReferenceEquals(_playCts, cts)) { _playCts = null; _overlay?.SetSpeaking(null, null); if (_mode == EditorMode.Listen && _side != null) ShowSidePanel(keepScroll: true); }
            cts.Dispose();
        }
    }

    private Task PlayFile(string path, CancellationToken ct)
    {
        _player ??= new MediaPlayer();
        var done = new TaskCompletionSource();
        void Ended(object? s, EventArgs e) => done.TrySetResult();
        void Failed(object? s, ExceptionEventArgs e) => done.TrySetException(new PlaybackException("This computer could not play the spoken audio: " + e.ErrorException.Message));
        _player.MediaEnded += Ended;
        _player.MediaFailed += Failed;
        var reg = ct.Register(() => { _player.Stop(); done.TrySetCanceled(); });
        _player.Open(new Uri(path));
        _player.Play();
        return done.Task.ContinueWith(t =>
        {
            reg.Dispose();
            _player.MediaEnded -= Ended;
            _player.MediaFailed -= Failed;
            return t;
        }, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
    }

    /// <summary>A kept piece that is empty or damaged is made again, once.</summary>
    private async Task<string> FreshIfBroken(string file, ReadPart part, VoiceChoice v, CancellationToken ct)
    {
        try { if (new FileInfo(file).Length > 200) return file; } catch (IOException) { }
        try { File.Delete(file); } catch (IOException) { }
        return await AudioFor(part, v, ct);
    }

    /// <summary>Stops reading, saying why in the status bar when it was not the author's Stop.</summary>
    private void StopListening(string? why)
    {
        bool was = _playCts != null;
        StopListening();
        if (was && why != null) UpdateStatus(why);
    }

    /// <summary>Space plays or stops, Esc stops — in Listen mode.</summary>
    private bool ListenKey(System.Windows.Input.Key key)
    {
        if (_mode != EditorMode.Listen || _current is not Chapter ch) return false;
        if (key == System.Windows.Input.Key.Escape && _playCts != null) { StopListening(); ShowSidePanel(keepScroll: true); return true; }
        if (key == System.Windows.Input.Key.Space)
        {
            if (_playCts != null) { StopListening(); ShowSidePanel(keepScroll: true); }
            else if (_ai.CanSpeak && _book?.NarratorVoice != null) PlayFrom(ch, _listenFrom);
            return true;
        }
        return false;
    }

    private void StopListening()
    {
        _playCts?.Cancel();
        _player?.Stop();
        _playStatus = "";
        _overlay?.SetSpeaking(null, null);
    }
}
