# AuthorPlus — development plan (executable breakdown)

Numbered tasks per phase, with file targets and acceptance criteria. Task IDs are stable — do
not renumber. Status legend: `[ ]` not started · `[~]` in progress · `[x]` done.

Runtime target: **.NET 10** (`net10.0` / `net10.0-windows`), WPF. Toolchain on this machine
(verified 2026-09-02 for SMADA): SDK 10.0.400, VS 2022 17.14, Windows SDK 10.0.26100
(`makeappx`, `signtool`). Packaging follows SMADA's scripted MSIX route (`packaging/pack.ps1`),
not a `.wapproj`.

**Every phase ends with something Ron can open and hand-test**, not just green tests.

---

## Phase 0 — Foundations (2026-09-04)

Exit: solution builds from the CLI with warnings-as-errors, tests green, the app opens, and a
book can be created, written in, saved, closed and reopened.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 0.1 | Repo, solution, four projects, `Directory.Build.props` | root, `src/`, `tests/` | `dotnet build` clean | [x] |
| 0.2 | Domain model | `Core/Models/Book.cs` | Book/Chapter/Character/TimelineEvent/Plotline + convergences | [x] |
| 0.3 | Folder-per-book store | `Core/Services/BookStore.cs` | round-trip, ordering, deletion, corrupt-item tolerance — all unit-tested | [x] |
| 0.4 | AI layer from CIATLE.AICore, no Core dependency; current Claude model ids | `src/AuthorPlus.AI/*` | router builds all four providers; settings encrypted at rest; PreseMaker key borrowing tested | [x] |
| 0.5 | WPF shell: tree + chapter rich-text editor + field forms + add/move/delete + save/open/recent | `App/MainWindow.*`, `PromptWindow.cs` | create a book, write two chapters, reorder, reopen — everything is where it was | [x] |
| 0.6 | AI Settings window + connection test; first two AI actions (chapter summary, character profile suggestions) | `App/AiSettingsWindow.*`, `MainWindow` AI menu | test button answers "OK"; summary lands in the chapter's Summary field | [x] |
| 0.7 | Docs: CLAUDE.md, README, this plan | root, `docs/` | | [x] |
| 0.8 | App icon + `app.manifest` (DPI aware, Windows 10/11 compat) | `App/` | crisp on 150 %/200 % displays | [ ] |
| 0.9 | Initial commit pushed to github.com/reaglin/AuthorPlus; app verified opening a sample book from the command line (`AuthorPlus.exe "<book folder>"`) | | screenshot checked 2026-09-04 | [x] |
| 0.10 | Hand-test on Ron's machine; capture first impressions into Phase 1 | | Ron's notes appended below | [ ] |

## Phase 1 — Manuscript

### 1A — Structure, the real book, AI Manager (added 2026-09-12; do these first)

Re-plan with Ron 2026-09-12: the tree becomes Book → Sections → Chapters → Items, Ron's
trilogy is the live test case (`docs/TRILOGY-TEST-CASE.md`), and AI moves to the shared
`Eaglin.AiManager` package (`..\AiManager`, built first — its Phases 0–3 gate task 1.15).
Tasks 1.1–1.9 below stay queued behind these; 1.7 is superseded by 1.12/1.13.

Exit: the trilogy is loaded as one book with three sections and 73 chapters; a chapter's
summary, analysis (from two providers) and characters-in-chapter exist as items under it;
AI keys were entered only once, in AI Manager.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 1.10 | **Sections.** `Section { Id, Title, Summary, Notes }`; `Book.Sections` + `SectionOrder`; **a chapter carries `SectionId`** (one order of truth: `Book.Chapters`) rather than sections holding id lists; chapters may still sit directly under the book. `BookStore`: `sections/{id}.json`, `FormatVersion` 2, version-1 books load with chapters at book level | `Core/Models`, `Core/Services/BookStore.cs` | round-trip, ordering, migration — unit-tested | [x] 2026-09-12 |
| 1.11 | **Items.** `Item { Id, OwnerId, Kind (Summary, Analysis, CharactersInChapter, Notes), Title, Body, CreatedUtc, ModifiedUtc, Provider, Model, PromptName, CharacterIds, PovCharacterId }` in `items/{id}.json`; owners hold `ItemIds`; Summary is one-per-chapter and replaces `Chapter.Summary` (migrated into an item) | same | round-trip; owner delete removes its items; migration test | [x] 2026-09-12 |
| 1.12 | **DOCX reader** in Core: `System.IO.Compression` + `XmlReader` → paragraphs, heading styles, bold/italic runs → FlowDocument XAML + word count. No NuGet package | `Core/Services/Import/DocxReader.cs` | fixture docx (built in the test) round-trips text, heading, bold, italic | [x] 2026-09-12 |
| 1.13 | **Manuscript import**: a folder of `Chapter N - Title.docx` → chapters in a chosen or new section; N numeric or spelled out (`Twenty-Eight`, `14-`); subfolders ignored; preview list (number, title, words) before import; summary reports gaps (e.g. missing 23) and heading/file-name mismatches; originals untouched | `Core/Services/Import/ManuscriptImporter.cs`, `App/ImportWindow` | file-name parser unit-tested on all 73 real names; import of the three parts matches `TRILOGY-TEST-CASE.md` | [x] 2026-09-12 |
| 1.14 | **Tree rebuild** (CIATLE `NavigationTree` style): Book root → Sections → Chapters → Items, then Characters, Timeline, Plotlines; glyph per node type; context menus (+ Section, + Chapter, + Item ▸ kind, Rename, Move up/down, Delete with confirm); item editors per kind; Section editor (title, summary, notes) | `App/MainWindow.*`, new `App/Views/*` | hand-test script in `TRILOGY-TEST-CASE.md` steps 1–2, 5 | [x] 2026-09-12 |
| 1.15 | **Adopt `Eaglin.AiManager`** (AiManager Phase 3): package reference from `C:\nuget-local`; delete `src/AuthorPlus.AI` + `AiTests.cs`; AI Settings / Prompt Library / Dashboard menu items open the package windows; register the app's templates | `App/`, `AuthorPlus.sln` | AI menu works with keys entered only in AiManagerApp; tests green | [x] 2026-09-12 |
| 1.16 | **AI actions that produce items**: Summary (creates/refreshes the singleton), Analysis (new dated item per run; pick one provider or "every provider with a key", streamed in `AiRunPanel` with Stop), Characters in this chapter (AI proposal merged with a name scan against the Characters list; user confirms) | `App/AiActions.cs`, templates | step 3–4 of the hand-test script | [x] 2026-09-12 |
| 1.17 | **Section-level items**: Outline (Ron's numbered chapter-flow style) and Section summary built from the chapter summaries; continuity prompt takes earlier sections' summaries as context | templates, `App` | Part 2 outline resembles `Chapter Summary - Power of 2.txt` in shape | [x] 2026-09-12 |
| 1.18 | **Load the trilogy** and record Ron's notes: three imports, counts checked, chapter-23 question answered, first-impression notes appended below | | notes dated in `TRILOGY-TEST-CASE.md` | [x] 2026-09-12 |
| 1.19 | **Add a chapter written elsewhere** (Ron, 2026-09-12: authors draft upcoming chapters or fill gaps on whatever device is handy). Context menu on a section or chapter: "Insert chapter from file…" — one DOCX, TXT or Markdown file → a new chapter at a chosen position (before/after the selected chapter, or by its number), the rest renumbered; the missing chapter 23 of *The Soul of Three* is the first real case | `Core/Services/Import/*`, `App` | insert at position round-trips; word count and title set; originals untouched | [x] 2026-09-12 |

### 1B — Manuscript polish

Exit: a novelist could draft a whole book here and not miss Word for the drafting stage.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 1.1 | Autosave (idle timer + on focus loss) and a visible "saved" indicator | `MainWindow` | pull the plug mid-sentence; lose at most a few seconds | [x] 2026-09-12 |
| 1.2 | Formatting toolbar: headings (chapter title / scene break), font size, lists, undo/redo, find & replace | chapter editor | | [x] 2026-09-12 |
| 1.3 | Scene breaks within a chapter (`* * *`) — **decided: prose stays flat**; a toolbar button inserts a centred `* * *` paragraph, exports recognise it (`FlowDocumentXaml.IsSceneBreak`). No scene list in the tree (chapters are the unit) | editor toolbar, exporters | | [x] 2026-09-12 |
| 1.4 | Drag-and-drop reorder in the tree (chapters onto chapters/sections/book, sections, items within an owner, characters/events/plotlines); multi-select delete **not done** (WPF TreeView is single-select; revisit if asked) | `MainWindow` | | [x] 2026-09-12 (DnD) |
| 1.5 | Word-count goals: per chapter and per book, daily progress (words added today) | `Book.TargetWords`, a `progress.json` in the book folder | status bar shows today / total / target | [x] 2026-09-12 |
| 1.6 | Export: Markdown and DOCX (whole book, in chapter order, with a title page) | `Core/Services/Export/*` | opens in Word with headings | [x] 2026-09-12 |
| 1.7 | ~~Import a manuscript: DOCX or Markdown → chapters by heading~~ superseded by 1.12/1.13 (per-chapter files); the "one compiled DOCX split on headings" case stays here as a later add-on | `Core/Services/Import/*` | | [ ] |
| 1.8 | Book properties page (title, author, synopsis, genre, target) as a tree root item instead of a dialog | | done with the tree rebuild (1.14) | [x] 2026-09-12 |
| 1.9 | Distraction-free / full-screen writing mode | `MainWindow` | F11 | [x] 2026-09-12 |

## Phase 2 — Story bible

Exit: characters, timeline and plotlines are linked to chapters and to each other, and the app
can show what is inconsistent.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 2.1 | Cross-links UI: chapter ↔ POV character / characters present / plotlines; event ↔ chapters & characters | field forms → pickers | | [x] 2026-09-12 |
| 2.2 | Character mentions: scan chapter text for character names, offer to link; "appears in" list on the character | `Core/Services/MentionFinder` | unit-tested on aliases | [x] 2026-09-12 |
| 2.3 | Timeline view: ordered table with When / Title / chapters / characters; reorder; "chapter order vs story order" side-by-side | new `TimelineView` | | [x] 2026-09-12 |
| 2.4 | Plotline board: per plotline the chapters it runs through; convergence markers; unresolved plotlines flagged | new `PlotlineView` | | [x] 2026-09-12 |
| 2.5 | Consistency checks (rule-based, no AI): character in a chapter before introduced; event referencing a deleted chapter; plotline never resolved | `Core/Services/Consistency` | tests per rule | [x] 2026-09-12 |
| 2.6 | Notes / research items — done through Items: a Notes (or Outline) item can hang off the book, a section, a chapter, a character, an event or a plotline (right-click › Add Notes) and shows under it in the tree; no separate fifth section needed | Models, `BookStore` owners | | [x] 2026-09-12 |

## Phase 3 — AI assistance

Exit: the AI features are the reason to buy the app, and every one is transparent about what it
sent and what it cost.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 3.1 | Prompt library: editable prompt templates (summarize, profile, continuity, style) with a preview of the exact payload before sending | `AI/Prompts`, a Prompt window | | [x] 2026-09-13 |
| 3.2 | Continuity check: given selected chapters + character/timeline facts, list contradictions with chapter references | | | [x] 2026-09-13 |
| 3.3 | Style analysis: sentence-length, passive voice, adverb density (local, no AI) + AI voice/tone read on request | `Core/Services/Style` local metrics tested | | [x] 2026-09-13 |
| 3.4 | Plot analysis: acts / tension map from summaries; suggested convergences | | | [x] 2026-09-13 |
| 3.5 | Streaming responses with cancel; per-call cost shown from usage; monthly spend total in AI Settings | all in the AI Manager: streaming via `AiRunPanel`, cost on every response and in the status bar, "Spent this month" line in AI Settings (AiManager 1.1.0) | | [x] 2026-09-13 |
| 3.6 | Long-chapter handling: chunking with a running summary when text exceeds the model's practical input | `Core/TextChunker`, `AiPrompts.MaxPromptChars` (≈100k tokens), templates `chunk-summary` + `merge-summaries` | a chapter over the ceiling is summarized in parts then merged; a book-level prompt over it is refused with an explanation | [x] 2026-09-13 |

## Phase 4 — Export, packaging, Store

Exit: signed MSIX installs on a clean Windows 11 VM; Store listing submitted.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 4.1 | EPUB and PDF export | `Core/Services/Export` (PDFsharp, MIT) | validates in an EPUB checker | [ ] |
| 4.2 | Packaging: `packaging/AppxManifest.xml`, `pack.ps1` (publish → makeappx → signtool), version from `Directory.Build.props` | `packaging/` | installs on a clean VM | [ ] |
| 4.3 | Privacy policy page on PunchMonkeyServer (`/privacy/authorplus`) + `PRIVACY_POLICY.txt` | | | [ ] |
| 4.4 | Store listing assets, screenshots, description; age rating; price | `store/` | | [ ] |
| 4.5 | Store name reservation (Ron) and first submission | | | [ ] |

## Version 2 — Audio (Ron, 2026-09-12: "not v1, but develop knowing where we are going")

Chapters and any selected text can be turned into audio through the AI Manager's text-to-speech
(AiManager Phase 5, its `docs/PLAN.md` §14). Designed now so v1 leaves room; built after v1 ships.

| # | Task | Targets | Notes |
|---|---|---|---|
| A.1 | `ItemKind.Audio`: an item under a chapter (or section, or the book) whose file lives in `audio\{itemId}.mp3` (or `.wav`); the item records voice, provider, characters spoken, cost, duration | `Core/Models`, `BookStore` (an `audio\` folder handled like chapter bodies: on disk, out of memory, deleted with the item) | the folder layout in CLAUDE.md idea 1 gets one more line |
| A.2 | "Read this chapter aloud…" → `AiHub.Speech.SynthesizeLongAsync` over the chapter's plain text with progress and cancel; result saved as an Audio item; play/stop in the item editor (`AudioPreviewPanel` from the WPF package); "Export audio…" copies the file out | `App` | v1's `ChapterText(ch)` is already the input |
| A.3 | Speak selection: right-click in the editor → "Read selection aloud" (plays, not saved) | `App` | |
| A.4 | Per-character voices: `Character.Voice` (voice id from the shared catalog) and speaker attribution of dialogue — a `SpeakerMap` per chapter (item kind or side file) built by an AI pass ("who says each quoted line") and confirmed by the author; multi-voice synthesis stitches segments | `Core`, templates, `App` | the hard part; do A.1–A.3 first |
| A.5 | Pronunciation dictionary shared with PreseMaker (names: "Olemelukwe", "Dalgo") — edited from the AI Manager's settings; per-book additions kept in the book folder | AiManager Phase 5.4 | |
| A.6 | Whole-book audiobook export: one file per chapter with a manifest, optional chapter intros | `Core/Services/Export` | |

## Later (captured, not scheduled)

- Multiple books open at once; a "series" folder with shared characters.
- Collaboration / cloud sync (folder-of-files makes OneDrive/Dropbox work today; conflict handling would be the feature).
- Localisation.

---

## Notes from hand-testing

- **2026-09-12 — Phase 1A built and the trilogy loaded.** Core: 47 tests green (sections,
  items, format-1 migration, DOCX reader on a fixture built in the test, every real chapter
  file-name shape, importer scan/import/insert). App: tree rebuilt as Book → Sections →
  Chapters (numbered per section) → Items, then Characters / Timeline / Plotlines; right-click
  menus per node; item editors per kind; Import window; Insert Chapter from File; AI actions
  Summarize / Analyze… (streams into `AiRunPanel`, one item per provider) / Characters in
  this chapter (name scan + AI) / Outline this section / Summarize this section. Loaded
  *The Book of One*: 28 + 19 + 26 = 73 chapters, 162,878 words, into
  `Documents\AuthorPlus\Books\The Book of One` (three sections); chapter 23 of Part 3 is
  reported missing by the scan (Ron is locating it — task 1.19 inserts it). Summarize on
  chapter 1 produced a Summary item (Claude, 7.1 s, 5,553 tokens, est. $0.037) and Save wrote
  `items\…json`. Observed: Part 2's in-document headings often differ from the file names
  ("Chapter Eight: The Interrogation" vs "Roland") — the Import window's "Titles from" choice
  covers it; the imported prose keeps Word's double spaces where the source has them.
  Still to hand-test by Ron: the Import window against Part 3 after chapter 23 turns up, and
  Analyze… on two providers.
- **Ron's to-do when back at the machine (2026-09-12):** (1) hand-test steps 3–4 of
  `TRILOGY-TEST-CASE.md` and write notes there; (2) find chapter 23 of *The Soul of Three* and
  insert it via right-click chapter 22 › Insert Chapter from File After This…; (3) decide
  whether Part 2 titles should follow the in-document headings; (4) the OpenAI account has no
  API credits; (5) AiManager PLAN.md §13 open points.
- **2026-09-12 — Phase 1B built (manuscript polish).** Autosave (12 s after the last edit,
  and when the window loses focus; "Autosaved hh:mm:ss" / "Unsaved changes" in the status
  bar); toolbar gains undo/redo, bullet and numbered lists, font size, Heading toggle, Scene
  break, Find…, a per-chapter word goal; Find and Replace panel (Ctrl+F; next / replace /
  replace all, case-insensitive); F11 distraction-free mode (Esc or F11 back); File › Export
  › Word (.docx) and Markdown (.md) — both written by hand in Core (`Export/`), the Word file
  read back by our own DOCX reader in tests; `progress.json` per book tracks words added
  today; drag-and-drop in the tree. Exported the loaded trilogy headless through the same
  code the menu calls: .docx 400 KB and .md 1.0 MB in 94 ms; the .docx read back by our reader
  shows 3 part headings + title, 73 chapter headings, 163,193 words (chapter headings add the
  ~300 extra words). 52 Core tests green. **Not hand-verified in the running app** (the window could
  not take the foreground during the automated check): the find panel, F11 and the export
  dialog — Ron, please try File › Export › Word on the trilogy and Ctrl+F in a chapter.
- **2026-09-12 — Phase 2 built (story bible).** Core: `MentionFinder` (names + aliases,
  whole words, longest name first so "Lucy" is not counted again inside "Lucy Dalgo") and
  `Consistency` (rules: dangling links, POV not present, plotline unresolved / without
  chapters, one-sided convergence, events told out of story order, missing summaries, and
  with the text scan: named before first linked chapter, never linked) — 59 tests green.
  App: `LinkPicker` checklists on chapters (characters present + POV, plotlines — kept in
  step with the Characters-in-chapter item and the plotlines' chapter lists), events
  (chapters, characters, plotlines) and plotlines (chapters, characters, convergences mirrored
  on both sides); Characters view (name, role, aliases, chapter count, POV count, first
  appearance), Timeline view (story order with move up/down beside "as the chapters tell it"
  with ⚠ for out-of-order), Plotline board (plotline × chapter grid, click to toggle, ◆ for
  convergences, red = unresolved); character page shows "Appears in" and "Scan chapters for
  mentions…" (`MentionsWindow`); Book › Check Consistency… (Ctrl+K, non-modal, double-click
  goes to the node, re-runs after deletes). Seeded the trilogy with its seven main
  characters and aliases and linked them by mention scan headless: 227 chapter-character
  links (Roland Ellison in 50 chapters, One in 68, Edmund Blackwood in 24…); the checker's
  first run reported 5 "named before first linked chapter" notes and the missing summaries.
  Verified by capture: chapter links, Characters view, character page, Timeline view; the
  Consistency window rendered from the assembly. Drag-and-drop, the plotline board with real
  plotlines, and the mentions dialog await Ron's hand-test.
- **2026-09-13 — Phase 3 built (AI assistance).** AiManager 1.1.0 adds `AiHub.EstimateTokens`
  / `EstimateInputCost` and the "Spent this month" line in AI Settings. AuthorPlus: View ›
  Preview AI Prompts Before Sending shows the exact system/user text, provider, model, token
  estimate and input cost for every request (whole calls and streamed runs alike — all go
  through one gate); `AiRunWindow` replaces the chapter-only analysis window and serves
  Analyze…, Continuity Check (AI)…, Plot Analysis (AI)… and the Style read, each saving dated
  Analysis items on the chapter / section / book; Style Report… (right-click a chapter) shows
  the local statistics (`Core/Style/StyleMetrics`: sentence lengths, passive-voice heuristic,
  -ly adverbs, dialogue share, Flesch, top words, repeated openings) with an "AI voice read"
  button; continuity and plot prompts work from chapter summaries plus the character records,
  timeline and plotlines, and warn when summaries are missing; chapters longer than the
  ceiling are summarized in parts (`TextChunker`) and merged, never cut. 72 Core tests green.
  Rendered headless over the trilogy: Style window on chapter 1, Prompt preview of the
  Part 1 continuity check. Ron: the AI runs themselves (continuity, plot, style read) are
  yours to try — start with Part 1 after summarizing its chapters.
