using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Audio;
using AuthorPlus.Core.Services.Export;
using Eaglin.AiManager;
using NAudio.MediaFoundation;
using NAudio.Wave;
using Brushes = System.Windows.Media.Brushes;

namespace AuthorPlus.App;

/// <summary>
/// The audiobook (Ron, 2026-11-06; plan phase 10): the book's pronunciation list (a page in the tree,
/// drafted by the AI, every entry heard before it is trusted), and Export audiobook — each chapter read as
/// Listen mode reads it, at the store standard (192 kbps MP3, 44.1 kHz, steady loudness, silence at the
/// head and tail), opening and closing credits, and one M4B with chapter marks. Only tracks whose sound
/// would change are made again; the pieces of a track being made are kept, so Stop loses nothing paid for.
/// </summary>
public partial class MainWindow
{
    private const string PronunciationsHeader = "Pronunciations";
    private const int StoreRate = 44100;
    private const int AskAtATime = 300;
    private TextBox? _focusWordBox;

    // ── Speaking with the pronunciation list (Listen mode and the audiobook) ─

    private bool TakesIpa(VoiceChoice v) =>
        GoogleVoiceNames.AcceptsPronunciations(v.Name) && _book!.VoiceLanguage.Equals("en-US", StringComparison.OrdinalIgnoreCase);

    /// <summary>The words as the voice is asked to say them: respelled, or with exact phonetics where the voice takes them.</summary>
    private SpokenText Spoken(string text, VoiceChoice v) => Pronunciations.Apply(text, _book!.Pronunciations, TakesIpa(v));

    private static AiSpeechRequest SpeechRequest(SpokenText spoken, VoiceChoice v, bool storeQuality, string purpose) => new()
    {
        Text = spoken.Text, Voice = v.Name, SpeakingRate = v.SpeakingRate, Purpose = purpose,
        Format = storeQuality ? AiAudioFormat.Wav : AiAudioFormat.Mp3,
        SampleRateHertz = storeQuality ? StoreRate : null,
        Pronunciations = spoken.Phonetics.Select(p => new AiPronunciation(p.Phrase, p.Ipa)).ToList()
    };

    /// <summary>A chapter's paragraphs as text (the open one from the editor).</summary>
    private List<string> ChapterParagraphs(Chapter ch)
    {
        FlowDocument? doc = null;
        if (ReferenceEquals(_current, ch) && _rtb != null) doc = _rtb.Document;
        else
        {
            var xaml = _store.LoadChapterBody(_book!, ch);
            if (!string.IsNullOrEmpty(xaml)) try { doc = (FlowDocument)XamlReader.Parse(xaml); } catch { doc = null; }
        }
        return doc == null ? new() : AllParagraphs(doc.Blocks).Select(p => RunsOf(p).Text).ToList();
    }

    // ── The Pronunciations page ───────────────────────────────────────────────

    private UIElement BuildPronunciationsPage()
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        var narrator = _book!.NarratorVoice;
        bool ipa = narrator != null && TakesIpa(narrator);
        panel.Children.Add(new TextBlock
        {
            Text = "How the names, places and invented words of this book are said aloud — in Listen mode and in the audiobook. " +
                   "\"Say it as\" is a plain respelling every voice can read (oh-leh-meh-LOO-kway; capitals show you the stress, the voice reads it in lower case). " +
                   "\"Exact sounds\" are phonetic symbols (IPA): more exact, used by the voices that take them. " +
                   (narrator == null ? "Choose the narrator's voice in Listen mode to hear them." :
                    ipa ? $"The narrator's voice ({narrator.Label}) takes exact sounds, so they are used where you give them."
                        : $"The narrator's voice ({narrator.Label}) does not take exact sounds, so the respelling is used."),
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8)
        });
        int notChecked = _book.Pronunciations.Count(p => p.FromAi);
        if (notChecked > 0)
            panel.Children.Add(new TextBlock
            {
                Text = $"{notChecked} suggested by the AI and not checked yet — ▶ Hear each one, change it if it is wrong, and press ✓ when it sounds right.",
                TextWrapping = TextWrapping.Wrap, Background = Palette.UnlinkedBg, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 8)
            });

        var grid = new Grid();
        foreach (var w in new[] { 170.0, 210, 200, 0, 0, 0, 0 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : GridLength.Auto });
        void Cell(UIElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); grid.Children.Add(e); }
        grid.RowDefinitions.Add(new RowDefinition());
        int c0 = 0;
        foreach (var h in new[] { "Word in the book", "Say it as", "Exact sounds (IPA)" })
            Cell(new TextBlock { Text = h, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 4) }, 0, c0++);

        int r = 1;
        foreach (var p in _book.Pronunciations.ToList())
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var entry = p;
            TextBox Field(string value, Action<string> set, string tip, string name)
            {
                var box = new TextBox { Text = value, Margin = new Thickness(0, 2, 8, 2), Padding = new Thickness(3), ToolTip = tip };
                System.Windows.Automation.AutomationProperties.SetName(box, name);
                box.TextChanged += (_, _) => { set(box.Text); entry.FromAi = false; MarkDirty(); };
                return box;
            }
            var word = Field(p.Word, v => entry.Word = v, "The word exactly as the book writes it; whole words are matched (\"Dalgo's\" too)", "Word");
            word.LostFocus += (_, _) => RefreshTreeTexts();
            if (p.Word.Length == 0) _focusWordBox = word;
            Cell(word, r, 0);
            Cell(Field(p.SayAs, v => entry.SayAs = v, "A respelling: syllables joined by hyphens, the stressed one in capitals (oh-leh-meh-LOO-kway). Empty = say it as written.", "Say it as"), r, 1);
            Cell(Field(p.Ipa, v => entry.Ipa = v, "Optional phonetic symbols (IPA), for the voices that take them. The AI can suggest them.", "Exact sounds"), r, 2);
            var hear = PanelButton("▶ Hear", "Says the word in the narrator's voice, as the audiobook will", () => HearWord(entry));
            hear.Margin = new Thickness(0, 2, 6, 2);
            Cell(hear, r, 3);
            if (p.FromAi)
            {
                var ok = PanelButton("✓", "It sounds right: keep it as yours (it is no longer marked as the AI's)", () => { entry.FromAi = false; MarkDirty(); ShowCurrent(); });
                ok.Margin = new Thickness(0, 2, 6, 2);
                Cell(ok, r, 4);
            }
            var del = PanelButton("Remove", $"Take \"{p.Word}\" off the list (it is then said as written)", () =>
            {
                if (entry.Word.Trim().Length > 0 && MessageBox.Show(this, $"Take \"{entry.Word}\" off the list? It is then said as written.", "Pronunciations",
                                                                     MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _book.Pronunciations.Remove(entry); MarkDirty(); RefreshTreeTexts(); ShowCurrent();
            });
            del.Margin = new Thickness(0, 2, 6, 2);
            Cell(del, r, 5);
            if (p.FromAi || p.Note.Length > 0)
                Cell(new TextBlock { Text = (p.FromAi ? "AI · " : "") + p.Note, Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 260,
                                     ToolTip = p.FromAi ? "Suggested by the AI; ✓ or editing it makes it yours" : null }, r, 6);
            r++;
        }
        panel.Children.Add(grid);
        if (_book.Pronunciations.Count == 0)
            panel.Children.Add(new TextBlock { Text = "No words yet. Suggest from the book lets the AI go through the characters' names and the places the book uses.", Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });

        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(PanelButton("+ Add a word", "A new line at the end of the list", AddPronunciation, primary: true));
        row.Children.Add(AiButton("Suggest from the book (AI)…", $"The AI goes through the characters' names, their other names and the places and capitalised words the book uses ({AskAtATime} at a time), and suggests how to say the ones a voice could get wrong",
                                  SuggestPronunciations));
        panel.Children.Add(row);
        if (_book.PronunciationsAsked.Count > 0)
            panel.Children.Add(new TextBlock { Text = $"The AI has looked at {_book.PronunciationsAsked.Count} words of the book so far; suggesting again asks about the next ones.", Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });

        var focus = _focusWordBox; _focusWordBox = null;
        focus?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => focus.Focus());
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void AddPronunciation()
    {
        _book!.Pronunciations.Add(new Pronunciation());
        MarkDirty();
        if (!ReferenceEquals(_current, PronunciationsHeader)) SelectNode(PronunciationsHeader); else ShowCurrent();
    }

    private async void HearWord(Pronunciation p)
    {
        if (p.Word.Trim().Length == 0) { UpdateStatus("Type the word first, then ▶ Hear."); return; }
        if (_book?.NarratorVoice is not { } v) { MessageBox.Show(this, "Choose the narrator's voice first: open a chapter, Listen mode (Ctrl+6) ▸ Voices ▸ Choose….", "Hear", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (!_ai.CanSpeak)
        {
            MessageBox.Show(this, "Hearing a word uses Google's voices. Add a Google text-to-speech key in AI Settings (the section \"Voices — Google text-to-speech\").", "Hear", MessageBoxButton.OK, MessageBoxImage.Information);
            AiSettings_Click(this, new RoutedEventArgs());
            return;
        }
        if (p.SayAs.Trim().Length == 0 && !(p.Ipa.Trim().Length > 0 && TakesIpa(v)))
            UpdateStatus(p.Ipa.Trim().Length > 0 ? "The narrator's voice does not take exact sounds and there is no respelling, so it is said as written." : "There is no respelling yet, so it is said as written.");
        else UpdateStatus($"Getting \"{p.Word.Trim()}\" from Google…");
        try
        {
            var spoken = Spoken($"{p.Word.Trim()}. I said, {p.Word.Trim()}.", v);
            var path = Path.Combine(_book.FolderPath!, "audio", "cache", Dialogue.AudioKey(v.Name, v.SpeakingRate, spoken.Key) + ".mp3");
            if (!File.Exists(path))
            {
                var speech = await _ai.SpeakAsync(SpeechRequest(spoken, v, storeQuality: false, "pronunciation"));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, speech.Audio);
                if (speech.PronunciationsIgnored) UpdateStatus("Google did not take the exact sounds for that word, so it was said as written — check the IPA, or use the respelling.");
            }
            await PlayFile(path, CancellationToken.None);
        }
        catch (AiException ex) { MessageBox.Show(this, ex.Message, "Hear", MessageBoxButton.OK, MessageBoxImage.Warning); }
        catch (Exception ex) when (ex is IOException or PlaybackException) { MessageBox.Show(this, ex.Message, "Hear", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SuggestPronunciations()
    {
        if (_book == null) return;
        CommitCurrent();
        var candidates = Pronunciations.Candidates(_book, _book.Chapters.Select(ChapterText));
        if (candidates.Count == 0)
        {
            MessageBox.Show(this, _book.PronunciationsAsked.Count > 0 ? "The AI has looked at every name the book uses. Add any it missed with + Add a word."
                                                                      : "Every name the book uses is on the list already.", "Pronunciations", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!EnsureAiOrExplain()) return;
        var batch = candidates.Take(AskAtATime).ToList();
        var values = new { book = _book.Title, synopsis = _book.Synopsis.Length > 0 ? _book.Synopsis : "(none)", words = string.Join("\n", batch) };
        string? reply = null;
        var dlg = new AiRunWindow(_ai, _book, _book.Id, ItemKind.Notes, "Pronunciations — suggest from the book", "Pronunciations", AiPrompts.Pronunciations,
            $"The AI looks at {(candidates.Count > AskAtATime ? $"the next {AskAtATime} of {candidates.Count}" : $"{batch.Count}")} names and words from the book and suggests how to say the ones a voice " +
            "could get wrong. They are added to the list, marked as the AI's; hear each one before you trust it. The prompt is \"pronunciations\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.Pronunciations).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: hear them →", handleResponse: r => reply = r.Text) { Owner = this };
        dlg.ShowDialog();
        if (reply == null) return;
        var known = _book.Pronunciations.Select(p => p.Word.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = Pronunciations.Parse(reply).Where(p => known.Add(p.Word.Trim())).ToList();
        _book.Pronunciations.AddRange(added);
        _book.PronunciationsAsked.AddRange(batch);                   // asked: the next run asks about the next words
        MarkDirty();
        RefreshTreeTexts();
        if (ReferenceEquals(_current, PronunciationsHeader)) ShowCurrent(); else SelectNode(PronunciationsHeader);
        var more = candidates.Count - batch.Count;
        UpdateStatus((added.Count == 0 ? "The AI found nothing a voice would get wrong in those words." : $"{added.Count} words added, marked as the AI's — ▶ Hear each, ✓ when it sounds right.") +
                     (more > 0 ? $" {more} more words to ask about: Suggest again." : ""));
    }

    // ── Export audiobook ──────────────────────────────────────────────────────

    /// <summary>What a made track was made from, in parts, so a remake can say why.</summary>
    private sealed class TrackNote
    {
        public string Fingerprint { get; set; } = "";
        public string Title { get; set; } = "";
        public string Words { get; set; } = "";
        public string Voices { get; set; } = "";
        public string Said { get; set; } = "";
        public double Seconds { get; set; }
        /// <summary>The author heard the chapter in Listen mode and approved it for the audiobook (Ron, 2026-11-06).</summary>
        public bool Approved { get; set; }
        public DateTime? ApprovedUtc { get; set; }
    }

    private sealed record Piece(string Text, VoiceChoice Voice, SpokenText Spoken, int PauseMs);

    private sealed record TrackPlan(string Id, string Label, List<Piece> Pieces, string Fingerprint)
    {
        public int Characters => Pieces.Sum(p => p.Spoken.Text.Length);
        public string WordsHash => Hash(string.Join("\n", Pieces.Skip(1).Select(p => p.Text + "#" + p.PauseMs)));
        public string VoicesHash => Hash(string.Join("\n", Pieces.Select(p => p.Voice.Name + "|" + p.Voice.SpeakingRate)));
        public string SaidHash => Hash(string.Join("\n", Pieces.Select(p => p.Spoken.Key)));
        public string TitleText => Pieces.Count > 0 ? Pieces[0].Text : "";
        private static string Hash(string s) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)))[..16];
    }

    private string AudioFolder(string sub) => Path.Combine(_book!.FolderPath!, "audio", sub);

    /// <summary>A chapter's pieces as the audiobook reads them: the part's and chapter's titles, then the chapter as Listen reads it.</summary>
    private TrackPlan ChapterPlan(Chapter ch, VoiceChoice narrator)
    {
        var paras = ChapterParagraphs(ch);
        var plan = Dialogue.Plan(paras, SpeakersOf(ch));
        var pieces = new List<Piece>();
        var section = _book!.SectionOf(ch);
        bool firstOfSection = section != null && _book.Chapters.FirstOrDefault(c => c.SectionId == section.Id) == ch;
        var title = (firstOfSection ? section!.Title + ". " : "") + $"Chapter {_book.ChapterNumber(ch)}. {ch.Title}.";
        pieces.Add(new Piece(title, narrator, Spoken(title, narrator), 0));
        ReadPart? prev = null;
        foreach (var part in plan)
        {
            var v = VoiceFor(part, narrator);
            bool scene = prev != null && Enumerable.Range(prev.Paragraph + 1, Math.Max(0, part.Paragraph - prev.Paragraph - 1))
                                                   .Any(i => paras[i].Trim() == FlowDocumentXaml.SceneBreak);
            int pause = prev == null ? ChapterSound.AfterTitleMs : ChapterSound.PauseBefore(prev, part, scene);
            pieces.Add(new Piece(part.Text, v, Spoken(part.Text, v), pause));
            prev = part;
        }
        var label = $"{_book.ChapterNumber(ch)}. {ch.Title}";
        return new TrackPlan(ch.Id.ToString("N"), label, pieces, ChapterSound.Fingerprint(title, pieces.Select(p => (p.Voice.Name, p.Voice.SpeakingRate, p.Spoken.Key + "#" + p.PauseMs))));
    }

    private TrackPlan CreditPlan(string id, string label, string text, VoiceChoice narrator)
    {
        var pieces = new List<Piece> { new(text, narrator, Spoken(text, narrator), 0) };
        return new TrackPlan(id, label, pieces, ChapterSound.Fingerprint(label, pieces.Select(p => (p.Voice.Name, p.Voice.SpeakingRate, p.Spoken.Key))));
    }

    private string OpeningCredit() => _book!.OpeningCredits.Trim().Length > 0 ? _book.OpeningCredits.Trim()
        : $"{_book.Title}. Written by {(_book.Author.Length > 0 ? _book.Author : "the author")}. Narrated by a synthesized voice.";

    private string ClosingCredit() => _book!.ClosingCredits.Trim().Length > 0 ? _book.ClosingCredits.Trim()
        : $"This has been {_book.Title}, written by {(_book.Author.Length > 0 ? _book.Author : "the author")}. Narrated by a synthesized voice. The end.";

    // ── A track's files (shared by Listen's Approve and the export) ───────────

    private sealed class Spent { public double Usd; }

    private string TrackNotePath(string id) => Path.Combine(AudioFolder("chapters"), id + ".json");
    private string TrackMp3(string id) => Path.Combine(AudioFolder("chapters"), id + ".mp3");

    private TrackNote? TrackNoteOf(string id)
    {
        try { return File.Exists(TrackNotePath(id)) && File.Exists(TrackMp3(id)) ? JsonSerializer.Deserialize<TrackNote>(File.ReadAllText(TrackNotePath(id))) : null; }
        catch { return null; }
    }

    private void SaveTrackNote(TrackPlan t, bool approved)
    {
        double secs; using (var r = new MediaFoundationReader(TrackMp3(t.Id))) secs = r.TotalTime.TotalSeconds;
        File.WriteAllText(TrackNotePath(t.Id), JsonSerializer.Serialize(new TrackNote
        {
            Fingerprint = t.Fingerprint, Title = t.TitleText, Words = t.WordsHash, Voices = t.VoicesHash, Said = t.SaidHash, Seconds = secs,
            Approved = approved, ApprovedUtc = approved ? DateTime.UtcNow : null
        }));
    }

    /// <summary>Why a made track would be made again, in a few words; null when it would not.</summary>
    private string? WhyRemake(TrackPlan plan)
    {
        var note = TrackNoteOf(plan.Id);
        if (note == null) return "not made yet";
        if (note.Fingerprint == plan.Fingerprint) return null;
        var why = note.Title.Length > 0 && note.Title != plan.TitleText ? "its number or title changed"
                : note.Words.Length > 0 && note.Words != plan.WordsHash ? "its text or speakers changed"
                : note.Voices.Length > 0 && note.Voices != plan.VoicesHash ? "a voice or pace changed"
                : note.Said.Length > 0 && note.Said != plan.SaidHash ? "a pronunciation changed"
                : "changed since it was made";
        return note.Approved ? why + " since it was approved" : why;
    }

    /// <summary>What it costs to make a track (characters and price; null price when a voice's is not known).</summary>
    private (int Chars, double? Cost) TrackCost(TrackPlan t)
    {
        double? cost = 0;
        foreach (var p in t.Pieces) { var c = _ai.EstimateSpeech(p.Voice.Name, p.Spoken.Text.Length); cost = c == null || cost == null ? null : cost + c; }
        return (t.Characters, cost);
    }

    /// <summary>
    /// One track: its pieces from Google (three at a time), each kept on disk as it arrives — so a Stop loses
    /// nothing paid for — then joined with their pauses, evened, encoded to 192 kbps MP3.
    /// </summary>
    private async Task MakeTrack(TrackPlan t, CancellationToken ct, Spent spent, Action<int, int>? progress)
    {
        var work = Path.Combine(AudioFolder("work"), t.Id);
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(AudioFolder("chapters"));
        string PiecePath(Piece p) => Path.Combine(work, Dialogue.AudioKey(p.Voice.Name, p.Voice.SpeakingRate, p.Spoken.Key) + ".wav");
        using var gate = new SemaphoreSlim(3);
        int finished = 0;
        await Task.WhenAll(t.Pieces.Select(async p =>
        {
            var path = PiecePath(p);
            if (!File.Exists(path))
            {
                await gate.WaitAsync(ct);
                try
                {
                    var speech = await _ai.SpeakAsync(SpeechRequest(p.Spoken, p.Voice, storeQuality: true, "audiobook"), null, ct);
                    await File.WriteAllBytesAsync(path + ".tmp", speech.Audio, CancellationToken.None);
                    File.Move(path + ".tmp", path, overwrite: true);
                    spent.Usd += speech.EstimatedCostUsd ?? 0;
                }
                finally { gate.Release(); }
            }
            progress?.Invoke(++finished, t.Pieces.Count);
        }));
        ct.ThrowIfCancellationRequested();
        var mp3 = TrackMp3(t.Id);
        await Task.Run(() =>
        {
            MediaFoundationApi.Startup();
            var sounds = t.Pieces.Select(p => (Wav.Read(File.ReadAllBytes(PiecePath(p))), p.PauseMs)).ToList();
            var joined = ChapterSound.Even(ChapterSound.Join(StoreRate, sounds));
            var stereo = new byte[joined.Samples.Length * 4];
            for (int i = 0; i < joined.Samples.Length; i++)
            {
                var v = joined.Samples[i];
                stereo[4 * i] = stereo[4 * i + 2] = (byte)v;
                stereo[4 * i + 1] = stereo[4 * i + 3] = (byte)(v >> 8);
            }
            var tmp = mp3 + ".tmp.mp3";
            using (var src = new RawSourceWaveStream(new MemoryStream(stereo), new WaveFormat(StoreRate, 16, 2)))
                MediaFoundationEncoder.EncodeToMp3(src, tmp, 192000);
            File.Move(tmp, mp3, overwrite: true);
        }, CancellationToken.None);
        try { Directory.Delete(work, recursive: true); } catch (IOException) { }      // the track is made: its pieces are not needed
    }

    private void ExportAudiobook_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        CommitCurrent();
        new AudiobookExport(this).ShowDialog();
    }

    /// <summary>The Export audiobook window: what will be made, what it costs, then the making.</summary>
    private sealed class AudiobookExport : Window
    {
        private readonly MainWindow _w;
        private readonly Book _book;
        private readonly StackPanel _chapters = new();
        private readonly List<(Chapter Chapter, CheckBox Box, TextBlock State)> _rows = new();
        private readonly Dictionary<Guid, TrackPlan> _plans = new();
        private readonly CheckBox _credits, _m4b;
        private readonly TextBox _opening, _closing, _folder;
        private readonly TextBlock _cost = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4), FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _coverNote = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        private readonly TextBlock _progressText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        private readonly ProgressBar _progress = new() { Height = 14, Margin = new Thickness(0, 6, 0, 0), Maximum = 1 };
        private readonly Button _go, _stop, _open;
        private CancellationTokenSource? _cts;
        private bool _folderEdited;
        private (int Chars, double? Cost, int Pieces, int ToMake) _last;

        public AudiobookExport(MainWindow w)
        {
            _w = w; _book = w._book!;
            Owner = w; Title = $"Export audiobook — {_book.Title}";
            Width = 860; Height = Math.Min(760, SystemParameters.WorkArea.Height - 40); MinWidth = 640; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;

            var root = new DockPanel { Margin = new Thickness(16) };
            var head = new StackPanel();
            head.Children.Add(new TextBlock { Text = "Export audiobook", FontSize = 18, FontWeight = FontWeights.SemiBold });
            head.Children.Add(new TextBlock
            {
                Text = "Chapters you approved in Listen mode are used as they are. Each other chapter is read as Listen mode reads it — the narrator's voice, each character's for their lines, the speakers you confirmed, the Pronunciations list — " +
                       "and saved as an MP3 at the standard the audiobook stores ask for (192 kbps, 44.1 kHz, steady loudness, silence at the start and end). " +
                       "Then all of it is put together into one M4B file with chapter marks. Chapters already made and not changed since are not made (or paid for) again; " +
                       "Stop at any time and carry on later — nothing paid for is lost.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 10)
            });
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var bottom = new StackPanel();
            bottom.Children.Add(_cost);
            bottom.Children.Add(_progress);
            bottom.Children.Add(_progressText);
            var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            _go = new Button { Content = "Make the audiobook", Padding = new Thickness(14, 5, 14, 5), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
            ToolTipService.SetShowOnDisabled(_go, true);
            _stop = new Button { Content = "Stop", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false,
                                 ToolTip = "Stops soon. Finished chapters are kept, and so are the pieces of the chapter being made — Make the audiobook carries on from there" };
            _open = new Button { Content = "Open the folder", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Opens the folder the audiobook is saved in" };
            var close = new Button { Content = "Close", Padding = new Thickness(14, 5, 14, 5), IsCancel = true };
            _go.Click += async (_, _) => await Make();
            _stop.Click += (_, _) => { _cts?.Cancel(); _stop.IsEnabled = false; _progressText.Text = "Stopping…"; };
            _open.Click += (_, _) => { try { Directory.CreateDirectory(_folder!.Text); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_folder.Text}\"") { UseShellExecute = true }); } catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { } };
            close.Click += (_, _) => Close();
            Closing += (_, e) =>
            {
                if (_cts == null) return;
                if (MessageBox.Show(this, "The audiobook is still being made. Stop and close? Nothing paid for is lost; Make the audiobook carries on next time.", "Export audiobook",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
                else _cts.Cancel();
            };
            buttons.Children.Add(_go); buttons.Children.Add(_stop); buttons.Children.Add(_open); buttons.Children.Add(close);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            var body = new StackPanel();
            var pick = new WrapPanel();
            pick.Children.Add(new TextBlock { Text = "Chapters", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            void Tick(Func<Chapter, bool> which) { foreach (var r in _rows) r.Box.IsChecked = which(r.Chapter); Changed(); }
            pick.Children.Add(SmallButton("All", "Tick every chapter", () => Tick(_ => true)));
            pick.Children.Add(SmallButton("None", "Untick every chapter", () => Tick(_ => false)));
            pick.Children.Add(SmallButton("Approved only", "Tick only the chapters you approved in Listen mode, unchanged since", () =>
                Tick(c => _plans.TryGetValue(c.Id, out var pl) && _w.TrackNoteOf(pl.Id) is { Approved: true } n && n.Fingerprint == pl.Fingerprint)));
            foreach (var section in _book.Sections)
            {
                var s = section;
                pick.Children.Add(SmallButton($"{s.Title} only", $"Tick only the chapters of {s.Title}", () => Tick(c => c.SectionId == s.Id)));
            }
            body.Children.Add(pick);
            foreach (var ch in _book.Chapters)
            {
                var section = _book.SectionOf(ch);
                var box = new CheckBox { IsChecked = true, Content = $"{(section != null ? section.Title + " · " : "")}{_book.ChapterNumber(ch)}. {ch.Title}  ({ch.WordCount:N0} words)", Margin = new Thickness(0, 2, 10, 2) };
                box.Click += (_, _) => Changed();
                var state = new TextBlock { Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Text = "…" };
                var line = new DockPanel();
                DockPanel.SetDock(state, Dock.Right);
                line.Children.Add(state); line.Children.Add(box);
                _chapters.Children.Add(line);
                _rows.Add((ch, box, state));
            }
            body.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(1), Padding = new Thickness(8), MaxHeight = 280,
                                           Child = new ScrollViewer { Content = _chapters, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            _credits = new CheckBox { Content = "Opening and closing credits, read by the narrator (the stores ask for them; they say the narration is a synthesized voice)", IsChecked = true, Margin = new Thickness(0, 12, 0, 4) };
            _credits.Click += (_, _) => Estimate();
            body.Children.Add(_credits);
            _opening = Credit(w.OpeningCredit(), "What the opening credits say", v => _book.OpeningCredits = v);
            _closing = Credit(w.ClosingCredit(), "What the closing credits say", v => _book.ClosingCredits = v);
            body.Children.Add(Labelled("Opening:", _opening)); body.Children.Add(Labelled("Closing:", _closing));

            _m4b = new CheckBox { Content = "One M4B file of the whole audiobook, with chapter marks (Apple Books and most audiobook players)", IsChecked = true, Margin = new Thickness(0, 10, 0, 4) };
            body.Children.Add(_m4b);
            _folder = new TextBox { Text = _book.AudiobookFolder.Length > 0 ? _book.AudiobookFolder : DefaultFolder(""), Padding = new Thickness(3),
                                    ToolTip = "Where the MP3s, the M4B and the notes are saved. A selection of chapters gets its own folder, so it never mixes with the whole book" };
            System.Windows.Automation.AutomationProperties.SetName(_folder, "Save in");
            _folder.TextChanged += (_, _) => { if (_folder.IsKeyboardFocusWithin) _folderEdited = true; };
            body.Children.Add(Labelled("Save in:", _folder));
            body.Children.Add(_coverNote);
            body.Children.Add(new TextBlock
            {
                Text = "Where to publish: Google Play Books and Kobo take the numbered chapter MP3s; Apple Books takes the M4B or the MP3s (through an aggregator). " +
                       "Each wants a square cover of its own (2400 × 2400 pixels) and asks you to say the narrator is a synthesized voice. Audible (ACX) does not take " +
                       "synthesized narration from outside. A note saying this is saved with the audiobook.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 12, Margin = new Thickness(0, 10, 0, 0)
            });
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
            Loaded += async (_, _) => { await Plan(); Changed(); };
        }

        private static Button SmallButton(string text, string tip, Action click)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 4), ToolTip = tip };
            b.Click += (_, _) => click();
            return b;
        }

        private TextBox Credit(string text, string tip, Action<string> set)
        {
            var box = new TextBox { Text = text, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(3), ToolTip = tip };
            System.Windows.Automation.AutomationProperties.SetName(box, tip);
            box.LostFocus += (_, _) => { set(box.Text.Trim()); _w.MarkDirty(); Estimate(); };
            return box;
        }

        private static UIElement Labelled(string label, UIElement e)
        {
            var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var t = new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(t, Dock.Left);
            d.Children.Add(t); d.Children.Add(e);
            return d;
        }

        private static string Safe(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim().Length > 0 ? name.Trim() : "Audiobook";
        }

        /// <summary>The whole book's folder, or a folder of its own for a selection ("… — Part Two").</summary>
        private string DefaultFolder(string selection) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AuthorPlus", "Audiobooks", Safe(_book.Title + (selection.Length > 0 ? " — " + selection : "")));

        /// <summary>What the ticked chapters are, in words: "" for all of them, a part's title, or "selected chapters".</summary>
        private string Selection()
        {
            var ticked = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Chapter).ToList();
            if (ticked.Count == _rows.Count) return "";
            var sections = ticked.Select(c => c.SectionId).Distinct().ToList();
            if (sections.Count == 1 && sections[0] is { } sid && _book.Sections.FirstOrDefault(s => s.Id == sid) is { } sec &&
                _book.Chapters.Where(c => c.SectionId == sid).All(c => ticked.Contains(c)))
                return sec.Title;
            return ticked.Count == 1 ? $"{_book.ChapterNumber(ticked[0])}. {ticked[0].Title}" : "selected chapters";
        }

        private void Changed()
        {
            if (!_folderEdited) _folder.Text = Selection() is { Length: > 0 } sel ? DefaultFolder(sel) : _book.AudiobookFolder.Length > 0 ? _book.AudiobookFolder : DefaultFolder("");
            Estimate();
        }

        /// <summary>Works out every chapter's pieces once (the chapters are read from disk); a moment for a long book.</summary>
        private async Task Plan()
        {
            if (_book.NarratorVoice is not { } narrator) { Estimate(); return; }
            _cost.Text = "Working out what each chapter needs…";
            _go.IsEnabled = false;
            _plans.Clear();
            int i = 0;
            foreach (var (ch, _, state) in _rows)
            {
                var plan = _w.ChapterPlan(ch, narrator);
                _plans[ch.Id] = plan;
                state.Text = _w.WhyRemake(plan) is { } why ? why
                           : _w.TrackNoteOf(plan.Id) is { Approved: true } n ? $"approved, {TimeSpan.FromSeconds(n.Seconds):h\\:mm\\:ss}"
                           : $"made, not approved yet, {TimeSpan.FromSeconds(_w.TrackNoteOf(plan.Id)!.Seconds):h\\:mm\\:ss}";
                if (++i % 4 == 0) await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);   // the window stays alive
            }
            var cover = CoverOf(_book);
            _coverNote.Text = cover != null ? "Cover: the book folder's cover picture goes into the M4B."
                : "Cover: none — put a picture named cover.jpg (or cover.png) in the book's folder and it goes into the M4B. The stores take their own cover on upload.";
        }

        private List<TrackPlan> Tracks(VoiceChoice narrator)
        {
            var list = new List<TrackPlan>();
            if (_credits.IsChecked == true) list.Add(_w.CreditPlan("opening", "Opening credits", _w.OpeningCredit(), narrator));
            list.AddRange(_rows.Where(r => r.Box.IsChecked == true && _plans.ContainsKey(r.Chapter.Id)).Select(r => _plans[r.Chapter.Id]));
            if (_credits.IsChecked == true) list.Add(_w.CreditPlan("closing", "Closing credits", _w.ClosingCredit(), narrator));
            return list;
        }

        /// <summary>What making the ticked chapters will take: which are made already, the characters to send and their price, the time.</summary>
        private void Estimate()
        {
            string? blocked = _book.NarratorVoice == null ? "Choose the narrator's voice first: open a chapter, Listen mode (Ctrl+6) ▸ Voices ▸ Choose…"
                            : !_w._ai.CanSpeak ? "Connect Google voices first: Help ▸ AI Settings ▸ Voices — Google text-to-speech."
                            : _plans.Count == 0 ? "Working out what each chapter needs…" : null;
            if (blocked != null) { _cost.Text = blocked; _go.IsEnabled = false; _go.ToolTip = blocked; return; }
            var tracks = Tracks(_book.NarratorVoice!);
            int chars = 0, toMake = 0, pieces = 0; double? cost = 0; double seconds = 0;
            foreach (var t in tracks)
            {
                var note = _w.TrackNoteOf(t.Id);
                bool fresh = note != null && note.Fingerprint == t.Fingerprint;
                seconds += fresh ? note!.Seconds : t.Characters / 15.0;
                if (fresh) continue;
                toMake++; chars += t.Characters; pieces += t.Pieces.Count;
                var (_, c) = _w.TrackCost(t); cost = c == null || cost == null ? null : cost + c;
            }
            _last = (chars, cost, pieces, toMake);
            int chapters = tracks.Count(t => t.Id is not ("opening" or "closing"));
            var price = cost is { } usd ? $"about ${usd:0.00} at Google's price" : "a price that is not known for one of the voices";
            var making = TimeSpan.FromSeconds(pieces * 0.8 + toMake * 4);
            _cost.Text = chapters == 0 ? "Tick the chapters to put in the audiobook."
                : $"{chapters} chapter(s), about {TimeSpan.FromSeconds(seconds):h\\:mm} of listening. " +
                  (toMake == 0 ? "Everything ticked is made already — only putting it together is left (no cost)."
                               : $"{toMake} track(s) to make: about {chars:N0} characters, {price} (Google's free characters each month come first); about {Duration(making)} to make, with this computer on.");
            _go.IsEnabled = chapters > 0;
            _go.ToolTip = _go.IsEnabled ? "Makes the tracks that need it, then puts the audiobook together" : "Tick at least one chapter";
        }

        private static string Duration(TimeSpan t) => t.TotalMinutes < 2 ? "a minute" : t.TotalHours < 1.5 ? $"{t.TotalMinutes:0} minutes" : $"{t.TotalHours:0.0} hours";

        private async Task Make()
        {
            var narrator = _book.NarratorVoice!;
            var tracks = Tracks(narrator);
            var outDir = _folder.Text.Trim();
            if (_last.ToMake > 0 && (_last.Cost == null || _last.Cost > 0.25) &&
                MessageBox.Show(this, $"Make {_last.ToMake} track(s): about {_last.Chars:N0} characters, " +
                                      (_last.Cost is { } usd ? $"about ${usd:0.00}" : "an amount not known for one of the voices") +
                                      $" at Google's price (its free characters each month come first), about {Duration(TimeSpan.FromSeconds(_last.Pieces * 0.8 + _last.ToMake * 4))} with this computer on.\n\n" +
                                      "You can stop at any time without losing what is paid for. Go on?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                Directory.CreateDirectory(outDir); Directory.CreateDirectory(_w.AudioFolder("chapters"));
                var old = Directory.GetFiles(outDir, "*.mp3").Concat(Directory.GetFiles(outDir, "*.m4b")).ToList();
                if (old.Count > 0)
                {
                    if (MessageBox.Show(this, $"\"{outDir}\" already has an audiobook's files in it ({old.Count} MP3/M4B files). Replace them with this one?\n\n" +
                                              "(No: choose another folder under Save in.)", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    foreach (var f in old) File.Delete(f);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { MessageBox.Show(this, "That folder cannot be used: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (Selection().Length == 0) { _book.AudiobookFolder = _folderEdited ? outDir : ""; _w.MarkDirty(); }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _go.IsEnabled = false; _stop.IsEnabled = true;
            var spent = new Spent();
            try
            {
                MediaFoundationApi.Startup();
                int done = 0, total = tracks.Count + (_m4b.IsChecked == true ? 1 : 0);
                void Step(string what) { _progressText.Text = what; _progress.Value = done / (double)Math.Max(1, total); }

                foreach (var t in tracks)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_w.TrackNoteOf(t.Id)?.Fingerprint != t.Fingerprint)
                    {
                        Step($"{t.Label}: getting {t.Pieces.Count} pieces from Google…");
                        await _w.MakeTrack(t, ct, spent, (i, n) => _progressText.Text = $"{t.Label}: piece {i} of {n}…");
                        _w.SaveTrackNote(t, approved: false);
                    }
                    done++;
                    Step($"{t.Label} made.");
                }

                // The files for the stores: numbered MP3s, the chapter list, the note on where to publish.
                Step("Saving the MP3s…");
                var times = new List<ChapterMark>();
                var at = TimeSpan.Zero;
                int n = 0;
                foreach (var t in tracks)
                {
                    var number = t.Id == "opening" ? "00" : t.Id == "closing" ? "99" : (++n).ToString("00");
                    File.Copy(_w.TrackMp3(t.Id), Path.Combine(outDir, $"{number} {Safe(t.Label)}.mp3"), overwrite: true);
                    times.Add(new ChapterMark(t.Label, at));
                    using var reader = new MediaFoundationReader(_w.TrackMp3(t.Id));
                    at += reader.TotalTime;
                }
                var bookName = Safe(_book.Title + (Selection() is { Length: > 0 } sel ? " — " + sel : ""));
                File.WriteAllLines(Path.Combine(outDir, "chapters.txt"), times.Select(t => $"{t.Start:hh\\:mm\\:ss}  {t.Title}").Append($"{at:hh\\:mm\\:ss}  (end)"));
                File.WriteAllText(Path.Combine(outDir, "Where to publish.txt"),
                    $"{bookName} — audiobook made with Author+ ({DateTime.Now:yyyy-MM-dd}).\r\n\r\n" +
                    $"Narration: a synthesized voice (Google {narrator.Label}). Say so when you upload it.\r\n\r\n" +
                    "Google Play Books: upload the numbered MP3s (00 is the opening credits, 99 the closing), choose \"synthesized voice\" as the narrator, and a square cover of 2400 × 2400 pixels.\r\n" +
                    "Kobo (Writing Life): the numbered MP3s; list the narrator as a synthesized voice; a square cover.\r\n" +
                    "Apple Books: through an audiobook aggregator, with the narration declared as synthesized; the MP3s (or the M4B, if the aggregator takes it).\r\n" +
                    "Audible (ACX) does not accept synthesized narration produced outside it.\r\n" +
                    "Check each store's current rules before you publish.\r\n\r\n" +
                    $"The M4B ({bookName}.m4b) is the whole audiobook in one file with chapter marks, for listening and for sharing with readers.\r\n");

                if (_m4b.IsChecked == true)
                {
                    ct.ThrowIfCancellationRequested();
                    _stop.IsEnabled = false;
                    Step("Putting it all together into one M4B — this cannot be stopped part way, and takes a few minutes for a long book…");
                    var m4a = Path.Combine(_w.AudioFolder("work"), "book.m4a");
                    Directory.CreateDirectory(Path.GetDirectoryName(m4a)!);
                    var paths = tracks.Select(t => _w.TrackMp3(t.Id)).ToList();
                    await Task.Run(() =>
                    {
                        MediaFoundationApi.Startup();
                        using var joined = new JoinedMp3s(paths);
                        MediaFoundationEncoder.EncodeToAac(joined, m4a, 64000);
                    });
                    var details = new AudiobookDetails
                    {
                        Title = _book.Title + (Selection() is { Length: > 0 } s2 ? " — " + s2 : ""), Author = _book.Author, Narrator = "Synthesized voice (Google)", Year = DateTime.Now.Year.ToString(),
                        Comment = $"Narrated by a synthesized voice ({narrator.Label}). Made with Author+.", Cover = CoverOf(_book)
                    };
                    await Task.Run(() => Mp4Book.Write(m4a, Path.Combine(outDir, bookName + ".m4b"), times, details));
                    File.Delete(m4a);
                    done++;
                }
                _progress.Value = 1;
                _progressText.Text = $"The audiobook is made: {tracks.Count} tracks, {at:h\\:mm\\:ss} of listening" +
                                     (spent.Usd > 0 ? $", about ${spent.Usd:0.00} of speech made this time" : ", nothing new to pay for") + $". Saved in {outDir}.";
                _w.UpdateStatus("Audiobook made — " + outDir);
            }
            catch (OperationCanceledException) { _progressText.Text = "Stopped. Nothing paid for is lost: Make the audiobook carries on from here." + (spent.Usd > 0 ? $" About ${spent.Usd:0.00} of speech was made." : ""); }
            catch (AiException ex) { _progressText.Text = "Stopped: " + ex.Message; MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                _progressText.Text = "Stopped: " + ex.Message;
                MessageBox.Show(this, "The audiobook could not be finished: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _cts?.Dispose(); _cts = null;
                _stop.IsEnabled = false;
                await Plan();
                Estimate();
            }
        }

        private static byte[]? CoverOf(Book book)
        {
            foreach (var name in new[] { "cover.jpg", "cover.jpeg", "cover.png" })
            {
                var p = Path.Combine(book.FolderPath!, name);
                if (File.Exists(p)) return File.ReadAllBytes(p);
            }
            return null;
        }
    }

    /// <summary>The tracks' MP3s read one after another, as one stream of mono 44.1 kHz sound (for the M4B).</summary>
    private sealed class JoinedMp3s : WaveStream
    {
        private readonly List<string> _paths;
        private int _index = -1;
        private WaveStream? _current;
        private long _position;
        public override WaveFormat WaveFormat { get; } = new(StoreRate, 16, 1);
        public JoinedMp3s(List<string> paths) { _paths = paths; Next(); }

        private bool Next()
        {
            _current?.Dispose();
            _current = null;
            if (++_index >= _paths.Count) return false;
            var reader = new MediaFoundationReader(_paths[_index]);
            _current = reader.WaveFormat.Channels == 1 && reader.WaveFormat.SampleRate == StoreRate && reader.WaveFormat.BitsPerSample == 16
                ? reader
                : new MediaFoundationResampler(reader, WaveFormat) { ResamplerQuality = 60 }.ToWaveStream(reader);
            return true;
        }

        public override long Length => long.MaxValue / 4;
        public override long Position { get => _position; set { } }
        public override int Read(byte[] buffer, int offset, int count)
        {
            while (_current != null)
            {
                int n = _current.Read(buffer, offset, count);
                if (n > 0) { _position += n; return n; }
                if (!Next()) break;
            }
            return 0;
        }
        protected override void Dispose(bool disposing) { if (disposing) _current?.Dispose(); base.Dispose(disposing); }
    }
}

internal static class ResamplerExtensions
{
    /// <summary>A resampler as a WaveStream that disposes its source with it.</summary>
    public static WaveStream ToWaveStream(this MediaFoundationResampler resampler, WaveStream source) => new ResampledStream(resampler, source);

    private sealed class ResampledStream(MediaFoundationResampler resampler, WaveStream source) : WaveStream
    {
        public override WaveFormat WaveFormat => resampler.WaveFormat;
        public override long Length => long.MaxValue / 4;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) { int n = resampler.Read(buffer, offset, count); Position += n; return n; }
        protected override void Dispose(bool disposing) { if (disposing) { resampler.Dispose(); source.Dispose(); } base.Dispose(disposing); }
    }
}
