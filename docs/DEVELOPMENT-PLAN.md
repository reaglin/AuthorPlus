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

### Ron's first hand-test (2026-09-13, build 0.4.0) — tasks

Ron liked: the right-click menus on chapters ("great … good interface"), the Characters and
Plotlines checklists that appear on a chapter.

| # | Task (Ron's words → what changes) | Targets | Status |
|---|---|---|---|
| 3.7 | *"The summary should be formatted to give major bullet points … What occurs; Character actions (introduction and actions). The continuous chain of text is challenging to read."* → the `chapter-summary` prompt returns two headed lists (What happens / Characters, marking introductions); the Summary editor shows it as written | `AiPrompts`, item editor | [x] 2026-09-13 |
| 3.8 | *"The Analyze screen should give more detail on what the analysis will produce."* → the run window lists the aspects it will return and what follows | `AiRunWindow` note | [x] 2026-09-13 |
| 3.9 | *"The analysis should break these into separate items and each should have a 'Suggestions' button that shows suggestions on how to rewrite sections to address the items — a redesign of the Analysis results screen."* → `chapter-analysis` returns fixed `## ` sections (Pacing, Tension, Stakes, Point of view and voice, Dialogue, Continuity, Prose habits, Three changes); the Analysis editor shows one expandable card per aspect with **Suggestions…**, which runs `analysis-suggestions` for that aspect and saves a Suggestions item under the analysis | `Core/AnalysisSections`, `AiPrompts`, item editors, `ItemKind.Suggestions`, items may own items | [x] 2026-09-13 |
| 3.10 | *"Characters in the Chapter can simply be 'Chapter Characters'."* → renamed everywhere | menus, item title | [x] 2026-09-13 |
| 3.11 | *"Analyze should find characters and create the Characters submenu; it should also create the Plotlines submenu, adding any new plotlines to the plotlines list."* → after an analysis a structured `chapter-extract` pass lists characters (introduced / acting, POV) and plotlines; known ones are linked, new ones are offered in a checklist and added as Character / Plotline records; a Chapter Characters item and a **Chapter Plotlines** item (new kind) appear under the chapter | `AiPrompts`, `Core/ChapterExtract`, `Item.PlotlineIds`, `NewEntitiesWindow` | [x] 2026-09-13 |
| 3.12 | *"The program should always open on the previous project."* → startup opens the most recent book when no folder is given | `MainWindow` | [x] 2026-09-13 |
| 3.13 | *"Change the Suggestions… so that it goes with each individual item, not a single set for everything."* → the analysis prompt returns numbered points under each aspect; the Analysis editor shows one row per point with its own **Suggestions…** (the request carries only that point); saved as "Suggestions · Aspect · n" | `AnalysisSections.Points`, `AiPrompts`, `BuildAnalysisEditor` | [x] 2026-09-13 |
| 3.15 | *"The results should come out in a diff style screen (current on the left, changes on the right), each suggestion with a reasoning and an Apply … or Mark; the author can return to all marked sections and write in their own words … each gets a Resolve option."* → suggestions come back structured (ORIGINAL verbatim / REWRITE / WHY) and are stored as `SuggestionEntry` records with a status (open / applied / marked / resolved); the Suggestions item is a diff screen (`WordDiff`: removed words highlighted left, added right) with Apply, Mark, Resolve, Reopen and Go to passage; Apply finds the passage in the chapter (`PassageLocator` — tolerant of quotes, dashes, whitespace — mapped onto the FlowDocument by `PassageInDocument`) and replaces it, in the open editor or on disk; Mark shows a "Your rewrite" box with Apply mine; **Marked Passages…** (chapter right-click, Book menu, or a Suggestions item) lists every marked passage with suggestion and reasoning; any item can be marked Resolved (✓ in the tree) and Suggestions labels show open/applied/marked/resolved counts | `Core/Suggestions.cs`, `SuggestionViews.cs`, `MainWindow` (`ISuggestionActions`) | [x] 2026-09-13 |
| 3.16 | *"We will need a higher token count for Suggestions."* → suggestions and analyses were running out of room: `analysis-suggestions` and `suggestion-refine` 8,000 tokens, `chapter-analysis` 8,000, single-aspect analyses 6,000, continuity / plot / style 6,000–4,000; summaries and extractions raised too | `AiPrompts` | [x] 2026-09-14 |
| 3.17 | *"Once we get the results back … the actions for the user are vague. A 'Next' should be available."* → every run window now has a **Next: …** button that lights up when the run finishes and says what to do next ("read the analysis and ask for suggestions", "review the suggestions", "compare the new suggestions"); the analysis and suggestions screens open with a line saying what each button does | `AiRunWindow` (`NextRequested`), analysis + suggestions editors | [x] 2026-09-14 |
| 3.18 | *"The Suggestions should also have another AI button where the author can request another suggestion … Current text, Suggested Text, the Why and a text box for 'Author Request' … conveying seriousness, humor, anger, etc. … to allow the author guide (with the AI) the suggestions to their intent."* → **Ask again…** on every suggestion opens a form with the passage, the suggestion, its reasoning, 20 moods and 12 craft directions to pick from, and a box for what the author is trying to convey; the `suggestion-refine` prompt is told the author's intent governs, and returns fresh rewrites by different routes; they are inserted into the same Suggestions item under the one asked about, each recording the intent and request | `SuggestionRequestWindow`, `suggestion-refine`, `SuggestionEntry.{Intent,AuthorRequest,RefinesId}` | [x] 2026-09-14 |
| 3.19 | *"The analysis … can also have separate prompts for the pacing, tension, stakes."* → seven single-aspect prompts (`analysis-pacing`, `-tension`, `-stakes`, `-voice`, `-dialogue`, `-continuity`, `-prose`), each with its own craft instructions, reachable from AI › Analyze One Aspect, the chapter's right-click menu, and a **Go deeper…** button on each aspect card of an existing analysis; the chapter's Notes field is passed as what the author is aiming for | `AiPrompts.AspectTemplates`, `AnalyzeAspect` | [x] 2026-09-14 |
| 3.14 | *"I am not sure how to identify plotlines. The first chapter introduces one — the mystery of the message, a primary plotline."* → `Plotline.Kind` (Primary / Secondary / Subplot) with a chooser on the plotline page and on the board; the per-chapter pass after Analyze… names each plotline's kind and whether it is introduced there; new **Find Plotlines (AI)…** (section, book, or the Plotlines header) proposes the threads from the chapter summaries with kind and chapters, and adds and links the ones kept | `PlotlineFinder`, `find-plotlines` template, `AiFindPlotlines_Click` | [x] 2026-09-13 |
| 3.20 | *"When the author does 'Ask Again' … a better design would still be a block design for each current passage. The original would appear at the top of the block a single time, then each suggestion (with the buttons) would be in subblocks below … collapsible … We also need a dismiss button on the suggestions (with a confirm, since it deletes it) … it should remember the parameters of the 'Ask Again'."* → the Suggestions screen is now one block per passage: the passage shown once at the top, **Go to passage** and **Expand all / Collapse all** on the block, then one collapsible frame per suggestion, collapsed by default when a passage has more than one. A frame's header carries its number, "(asked again)" when it is a refinement, a preview of the rewrite and its status; inside it shows what the author asked for, the change as a single marked-up text rather than a repeat of the original, the reasoning, and Ask again… / Apply / Mark / Resolve / **Dismiss** (confirmed, then the suggestion is deleted and the rest renumbered). Six moods added: fear, curiosity, hate, love, regret (wonder was already there) | `SuggestionsEditor.{GroupByPassage,PassageBlock,Frame}`, `ISuggestionActions.Dismiss`, `MainWindow.Dismiss` | [x] 2026-09-14 |
| 3.21 | *"Have the large text box display the prompt when it is brought up (editable) … a more distinct 'Running Prompt' with the timer … replace label 'Run' with 'Send Prompt' … If a prompt is cut off due to lack of tokens, this needs to be highlighted."* → every AI screen is now a prompt console: it opens with the request itself in the box, editable, and the author's edits are what gets sent; **Send Prompt** empties the box, shows "Running prompt… 0.0s" counting in the accent colour, and refills it with the answer as it streams; **Edit prompt** brings the request back for a second send. A reply that stopped at the token ceiling reports it in red, with what to raise and where | `AiRunWindow`, AiManager `AiRunPanel.{ShowPrompt,PromptText,LastWasCutOff}`, `AiRequest.With(provider, model, user)` (AiManager 1.2.0) | [x] 2026-09-14 |
| 3.22 | *"The numbering on passages is confusing, instead of continuing at the last number from the previous passage change the numbering scheme P01 - Suggestion 1, P01 - Suggestion 2, … P02 - Suggestion 1."* → suggestions are numbered within their passage block, the block itself named P01, P02 (M01… in Marked Passages); the entry's global index is kept only for ordering | `SuggestionsEditor.{PassageBlock,Frame}` | [x] 2026-09-14 |
| 3.23 | *"We want the author to be able to select a passage for suggestions. Two possible approaches: highlight in a screen showing the chapters, OR by paragraph with a 'Select' for the paragraphs."* → **both**, meeting at one place. Selecting text in the chapter editor and pressing **Suggestions… (AI)** (toolbar) or AI › **Suggestions for Selection…** takes the selection, which works on part of a paragraph or across several; with nothing selected, and from AI › **Choose a Passage…** or a chapter's right-click menu, the paragraph picker opens — the chapter paragraph by paragraph with **Select** on each and tick-boxes to take a run of them together. Either way the author is asked what the passage is for (the same mood and craft chips as "Ask again", but blank is allowed on a first ask), and the new `passage-suggestions` prompt returns rewrites quoted from inside the chosen passage only, saved as a Suggestions item on the chapter | `PassagePickerWindow`, `SuggestionRequestWindow(chapter, passage, aspect)`, `AiPrompts.PassageSuggestions`, `MainWindow.{AiPassageSuggestions_Click,AiChoosePassage_Click,RunPassageSuggestions}` | [x] 2026-09-14 |
| 3.24 | *"In the main editor you have Characters and Plotlines under the edit bar. These can go into the status menu and link to the Characters and Plotlines pages."* → both checklists are gone from the chapter editor, which is now toolbar, find bar and manuscript. The status bar carries "Characters: n · POV name ▸" and "Plotlines: n ▸" for the open chapter, each a link to that page, each with the full list in its tooltip | `MainWindow.UpdateChapterLinks`, `StatusCharacters_Click`, `StatusPlotlines_Click` | [x] 2026-09-14 |
| 3.25 | *"The plotlines page already has per chapter information … we simply add this same type of chart to characters and we can see the characters in each chapter."* → the Characters page is now the cast table above and a `CharacterBoard` below — one row per character, one column per chapter, ● present, ★ point of view; a click puts a character in a chapter or takes them out, a double-click makes them its POV. That board and a character's own page are where these links are now made | `CharacterBoard`, `CharactersView` | [x] 2026-09-14 |
| 3.26 | *"The Chapter → Chapter plotlines is an analysis piece … We do not need a checkbox to add or exclude these. What we do want to see (in a table for clear viewing) is the Plotline short description, type, Introduced/Continued (from…), Description and a link to Plotlines."* → both chapter items are read-only tables. Chapter Plotlines: name (a link), type as a tinted chip, "Introduced here" / "Continued from ch. n" / "Named here, not linked", and the plotline's description. Chapter Characters: name (a link), role, "Point of view" / "Introduced here" / "Also in n other chapters", and the other names the text uses. The AI's per-chapter notes stay editable under each table | `MainWindow.BuildChapterPlotlinesEditor`, `BuildCharactersInChapterEditor` | [x] 2026-09-14 |
| 3.27 | *"Color coding backgrounds that perform functions and ensuring readability of the screens is important to the design."* → one palette for everything that carries meaning: plotline kind (primary / secondary / chapter / subplot / extra), introduced / continued / not-linked, point of view, table furniture and links. Every tint is pale enough to read near-black text through, and every tint sits behind a word that says the same thing, so nothing depends on colour alone. Both boards gained alternating row tints for scanning 73 columns. `PlotlineKind` gained **Chapter** and **Extra**, as Ron listed them | `Palette`, `PlotlineKind`, `PlotlineBoard` | [x] 2026-09-14 |
| 3.28 | *"The items are good — Role, Aliases, Origin, Description (split to Physical Description, Personality), Motivations, Actions, Arc, Notes. In the character screen these should appear as bullet points and should have an edit button."* → every long field on a character and a plotline is kept as plain text, one entry per line, and shown as bullets with an **Edit** button that opens the whole field in one box. `Character.Description` split into **PhysicalDescription** and **Personality** (format 2 → 3 folds the old field into Physical description on load) | `Bullets`, `BulletField`, `FieldEditWindow`, `MainWindow.BuildCharacterPage` | [x] 2026-09-14 |
| 3.29 | *"When the AI touches a character, it should be able to add an entry to be noted as added by AI — '(AI) 5 ft 11 in, with dark black hair' under physical description."* → an entry the AI wrote is a line that opens `(AI)`; the app shows it with an AI chip and a tint, and the author can change it, take the mark off or delete it like any other line. `character-profile` now returns `FIELD | one detail` lines that are filed into the right field, never overwriting what the author wrote, never repeating what is already there | `Bullets.Add`, `LabeledLines`, `AiCharacter_Click` | [x] 2026-09-14 |
| 3.30 | *"Plotlines are the same pattern, but Planned, Active, Resolved depend on chapter … The Summary should have bullet points … the AI should be able to add to that summary."* → the plotline page is the same bullet-and-Edit pattern over a status note, the summary and notes. A thread's status is now **derived**: planned until it runs anywhere, active once it does, resolved when a chapter resolves it; the page shows its life as "Introduced ch. 1, continuing through ch. 23, resolved ch. 25". New **Add to the summary from the chapters (AI)** reads the summaries of the chapters the thread runs through and adds what they say, marked as the AI's | `Book.{SeedRoles,SetRole,RecomputeStatus,RoleSpan}`, `plotline-summary`, `AiPlotlineSummary_Click` | [x] 2026-09-14 |
| 3.31 | *"The dot is not descriptive enough. We can use dots (color coded) for Introduced, Continuing, Resolved and clicking the dot allows the user to change the status."* → `PlotlineRole` per chapter (`Plotline.ChapterRoles`, seeded on load: the first chapter introduces, the rest continue). On the board a cell is a green, blue or purple dot; clicking it opens a small menu — introduced here / continuing here / resolved here / not in this chapter — and the thread's status and name colour follow immediately | `PlotlineRole`, `ChapterRole`, `PlotlineBoard` | [x] 2026-09-14 |
| 3.32 | *"Same pattern on dots for Characters."* → `CharacterRole` (Introduced / Appears / Leaves) per chapter on `Character.ChapterRoles`, seeded on load and after anything links a character to a chapter. The Characters chart uses the same three colours as the Plotlines chart — green they arrive, blue they are about, purple they leave — and a click opens the same kind of menu, with point of view as a fourth line in it (the star now keeps the colour of their part). The character page shows their life as "Introduced ch. 2, through ch. 40, leaves ch. 52", with a chip per chapter; the Chapter Characters table reads the part from the role rather than guessing | `CharacterRole`, `CharacterChapterRole`, `Book.{SeedRoles,SetRole,RoleSpan}`, `CharacterBoard` | [x] 2026-09-14 |

## Phase 4 — Export, packaging, Store

Exit: signed MSIX installs on a clean Windows 11 VM; Store listing submitted.

**Start with `docs/STORE-SUBMISSION.md`** — the whole submission in order, with the values to type
and the commands to run.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 4.1 | EPUB and PDF export. **EPUB 3** written by hand as a zip like the DOCX (`EpubWriter`): one XHTML per chapter, a title page, a page per part, a stylesheet, and both tables of contents — `nav.xhtml` and a `toc.ncx` for older readers. **PDF** typeset by `PdfBookWriter` on PDFsharp: title page, a page per part, every chapter opening on a new page after a drop, justified prose with a first-line indent, widow and orphan control, a page number at the foot, and PDF bookmarks mirroring the tree. Page setup (6×9 book / Letter / A4, straight or ragged margin, page numbers) is chosen in `ExportPdfWindow`. Fonts are read off the machine by `BookFontResolver`, so Core stays platform-neutral | `Core/Services/Export/{EpubWriter,PdfBookWriter,BookFontResolver}.cs`, `App/ExportPdfWindow.cs`, File › Export | **epubcheck 5.1.0 (EPUB 3.3): 0 fatals / 0 errors / 0 warnings** on the trilogy and on two edge-case books; the PDF is read back by PdfPig in the tests (text, geometry, bookmarks) | [x] 2026-09-14 |
| 4.2 | Packaging: `AppxManifest.xml`, `pack.ps1` (publish → makepri → makeappx → signtool), `make-msixupload.ps1` (bundle + symbols, unsigned as the Store wants, with a package-family-name check), `run-wack.ps1`, version from `Directory.Build.props`; `app.manifest` for PerMonitorV2 | `packaging/` | a 71.8 MB `.msixupload` builds end to end | [x] 2026-09-14 |
| 4.3 | Privacy policy: `PRIVACY_POLICY.md` here, `PrivacyAuthorPlus.razor` in the PunchMonkeyServer repo at `/privacy/authorplus`, listed on `/privacy` | | committed; deploy with that repo's `deploy.ps1` | [x] 2026-09-14 |
| 4.4 | Store art: `make-store-assets.ps1` draws the mark once and emits every tile at five scales, the taskbar target sizes, the five Partner Center listing images and `AuthorPlus.ico` — 52 assets, none over WACK's 204,800-byte cap | `packaging/Assets`, `store/images` | contact sheet reads at 16 px | [x] 2026-09-14 |
| 4.5 | The submission guide: identity, the order to do everything in, Partner Center field by field, age-rating answers, certification notes, listing copy ready to paste | `docs/STORE-SUBMISSION.md` | | [x] 2026-09-14 |
| 4.6 | Screenshots (≥ 1366×768, four or more) | `store/screenshots/`, `tools/store-shots.ps1`, `tools/sample-book/make-sample-book.cs`, `tools/tour-window.ps1` (from TrafficLab+) | seven at 1440 × 900 of a sample book written for the listing ("The Keeper of Gull Point" — not Ron's manuscript), opened from `C:\Users\Public\Documents` so no user name shows; chapter, AI analysis, AI summary, characters chart, plotlines chart, timeline, a character | ⚠️ made 2026-11-05 and uploaded to Partner Center — Ron to look |
| 4.7 | Deploy the privacy page; copy the package identity name out of Partner Center; set 1.0.0; build with the real identity; run WACK; submit | | Store listing live | ✅ 2026-11-05: identity `DeanEaglin.Author`, Store ID 9MZCBQVSSDVG (Ron); privacy live at softwareplus.ai/author/privacy/; 1.0.0 built with the real identity and the new icon (80.0 MB); **WACK PASS 23/24** (the usual "Blocked executables"; run by Ron on the build before the icon change — images only differ); **free** (Ron: *"We are going to start with free"*); Ron uploaded the package (validated). Partner Center filled in by Claude: pricing $0, all markets, public; Productivity; uses personal information → privacy URL; website and support contact; generative-AI declaration; age rating **3+ / Everyone** (Online Content = Yes for generated AI content; violence, sexual material, language and drugs = No, since the AI only works on the author's own text; Ron agreed to the IARC terms); English listing (description, short description, 18 features, 7 keywords, release notes "First release.", copyright, developer), 7 screenshots, poster art, box art and 300/150/71 tile icons; certification notes on Additional Testing Information. **Submitted for certification 2026-11-05** (Ron's go-ahead; publishes as soon as it passes). **Live on the Microsoft Store 2026-11-06** (Ron); tagged `v1.0.0`; softwareplus.ai/author/ and the home page tile switched to the Store link and deployed 2026-11-06 (`docs/STORE-SUBMISSION.md` §12) |
| 4.9 | **The icon carries the "+"** (Ron, 2026-09-29: *"for Author+ we will want to stick to the theme with the + in the icon"*). Ron, 2026-11-05: the softwareplus.ai icon is a good model — *"remove the blue background around the + and put the Author+ title on the icon"*. Two candidates drawn; **Ron chose B**: the gold "+" over the page corner with no navy plate, and "Author+" in white under a smaller page. The title is on every tile of 71 px and up, the listing images and the .ico's 64-256 frames; 16-48 px (taskbar, Start list) keep the page and "+" alone, where lettering cannot be read. The softwareplus.ai tile (`site/images/icons/author.png`) is written by the same script and deployed | `packaging/make-store-assets.ps1`, `packaging/Assets`, `store/images`, `src/AuthorPlus.App/AuthorPlus.ico` | 52 assets, none over WACK's cap | ⚠️ built 2026-11-05 — Ron to look at it installed |
| 4.8 | **Help › About says who made it.** Ron, 2026-09-25: *"Created by Dr. Ron Eaglin"* with his picture in every program's About. The message box became `AboutWindow`: his picture (256 px copy, shown at 96 px with rounded corners) beside the heading, the version read from the assembly (`AuthorPlusVersion`), two plain paragraphs on what AuthorPlus is for, and where the books are kept | `App/AboutWindow.cs`, `App/Images/dr-ron-eaglin.jpg`, `MainWindow.xaml.cs` | builds with no warnings, 137 tests green; photographed running | ⚠️ built — waiting for Ron to look (Help › About AuthorPlus) |
| 4.10 | **Help › About links to the program's page and all programs.** Ron, 2026-10-30: *"The Help->About on all the apps should have a link to the location of the app on the Windows App Store. They should also have a link to their subdirectory on SoftwarePlus.ai and a link to all programs."* Two links under "Your books are kept in": "More about AuthorPlus, with help and examples: softwareplus.ai/author" and "All of Dr. Ron Eaglin's programs: softwareplus.ai" (→ `softwareplus.ai/#programs`); real hyperlinks, reached with Tab, opened in the browser. No Store line yet — AuthorPlus has no Store ID (4.11) | `App/AboutWindow.cs` | builds with no warnings, 137 tests green; photographed running (the window grows to fit, nothing cut off) | ⚠️ built 2026-10-30 — Ron to look (Help › About AuthorPlus) |
| 4.11 | **The Microsoft Store link in About** — add the Store line once Partner Center gives the Store ID: "AuthorPlus on the Microsoft Store (updates, ratings and reviews): apps.microsoft.com/detail/{Store ID}", as Accessibility+ does (`..\CLAUDE.md`, the 2026-10-30 About rule) | `App/AboutWindow.cs` | the link opens the Store page | ⚠️ built 2026-11-05 (Store ID 9MZCBQVSSDVG) — Ron to look; the page answers once the listing is live |
| 4.12 | **Export Project / Import Project** (Ron, 2026-11-05: *"export and import an entire project to work on on another machine"*). File › **Export Project (to work on another computer)…** writes the whole book folder — with its saved versions (`.git`) — as one `.authorplus` file (a zip: `authorplus-project.json` + `book/…`; leaves out `*.tmp`, `progress.json` and exports, as versioning does); File › **Import Project…** says what is in the file and, when this computer has the same book (same id), how the two relate — from **content fingerprints** (book files hashed, `book.json`'s save time left out, so a re-save is not "writing") and a per-computer record of where each book stood at its last export/import (`%APPDATA%\AuthorPlus\transfers.json`): file newer → Replace; file older or both changed → Keep both, with a ⚠ saying why; several copies here → choose which. Replace never deletes: the old copy goes to `Documents\AuthorPlus\Replaced books\…`. Unpacked outside the library, checked to load, then moved in; entries outside the book folder refused. The window is locked while either runs. Real trilogy: 368 files, 1.1 MB, export 2.4 s, import 0.4 s, history intact, nothing pending after | `Core/Services/Transfer/ProjectPackage.cs`, `App/MainWindow.Project.cs`, `App/ImportProjectWindow.cs`, File menu | 11 new tests (148 green): round trip with history, what is left out, replace/keep both, wrong or damaged files, zip-slip, newer format, fingerprint ignores a re-save, every situation, copies after keep both, the transfer log. UX: walkthrough + heuristics — 0 critical, 5 major and 7 minor, all fixed (newer/older by fingerprint not save time; "Save changes?" asked before the comparison; window locked; choose among several copies; stale-file and same-computer wording; plain errors with "Details for support"; OneDrive hint; Insert Chapter pointer; "Author+ — …" titles; file name with time and computer; "write on one computer at a time"). "Project" kept (Ron's word) with "your whole book in one file" in the tooltips and the window | ⚠️ Ron: hand-test round 2026-11 A1–A6 (`docs/MANUAL-TESTING.md`) |
| 4.13 | **Help › Check for a new version…** (the 2026-10-30 portfolio rule; a new Store program has it from its first version): ported from Accessibility+ (`StoreUpdates.cs`); the App now targets `net10.0-windows10.0.19041.0` for `Windows.Services.Store`; a hand-test copy says it cannot check and offers the Store page. Privacy policy says it asks the Store only when chosen | `App/StoreUpdates.cs`, `MainWindow.xaml` | builds with no warnings | ⚠️ Ron: try it in the Store-installed copy after 1.0.0 is live |
| 4.14 | **The program says Author+** in front of a person (the "+" naming rule; it ships as Author+ at 1.0): window title, About (title, heading, text, links), menus, dialogs, GitHub steps. Code names, folders (`Documents\AuthorPlus`) and the GitHub token page's description are unchanged | `App/*` | | ⚠️ Ron to look |
| 4.15 | **NuGet audit fix**: PDFsharp 6.2.4 brings `System.Security.Cryptography.Pkcs` 8.0.1 (NU1902/NU1903, fatal under warnings-as-errors) — a direct reference to 10.0.13 in Core, as Retirement+ did | `Core/AuthorPlus.Core.csproj` | restore and build clean | [x] 2026-11-05 |

## Phase 5 — Versions and GitHub (2026-09-15)

Ron, 2026-09-15: *"integrate the book project in Git for management of the book and project and
push the repo to GitHub."* Decided with him the same day: **LibGit2Sharp** (so a Store buyer needs
no Git installed) and **plain-English versions + GitHub backup** rather than a git panel — the
design and the reasoning are in `docs/VERSIONS-AND-GITHUB.md`.

Exit: a book folder keeps its own history, an old draft of a chapter can be read against today's
and taken back, and the whole book — with its history — lives on a private GitHub repository that
is kept up to date without the author thinking about it.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 5.1 | `BookRepository`: init (branch `main`, `.gitattributes` `* -text`, `.gitignore` for `*.tmp`, `progress.json` and exports), pending changes, save a version, history, history of one file, changes in a version, read a file at a version, restore | `Core/Services/Versioning/BookRepository.cs` | round-trip on a real repository in a temp folder; a restore adds versions and loses nothing | [x] 2026-09-15 |
| 5.2 | `ChangeNames`: file paths → the book's own words ("Part One, Chapter 12 — The Interrogation (the text)", "Summary of …", "Character: …"), including the name of something the version removed, read from its record at the version before; and the version name offered when the author writes none | `Core/Services/Versioning/ChangeNames.cs` | unit-tested on every folder; "2 chapters and 1 character changed" | [x] 2026-09-15 |
| 5.3 | `TextDiff`: two versions of a chapter compared paragraph by paragraph **in the prose**, reusing the existing `DiffKind`/`DiffPiece`/`WordDiff` so a rewritten paragraph shows the words that moved | `Core/Services/Versioning/TextDiff.cs` | one reworked line out of three reads as one out, one in | [x] 2026-09-15 |
| 5.4 | GitHub: `GitHubApi` (who is this token / find a repository / create an empty private one) and `GitHubAccount` (the token at rest, DPAPI, `Documents\AuthorPlus\github.json`) | `Core/Services/Versioning/GitHub*.cs` | stub-transport tests for 401 / 403 / 422-name-taken / 404 / no network; the token is never in the file in the clear | [x] 2026-09-15 |
| 5.5 | Push, pull, and the clash: force-push ("keep this computer") or reset to GitHub after parking the local tip on a `kept-from-this-computer-…` branch ("keep GitHub"); every libgit2 failure turned into a sentence the author can act on | `BookRepository` | conflict leaves the folder untouched and asks | [x] 2026-09-15 |
| 5.6 | The UI: a `Versions` menu, the status-bar line, `VersionsWindow` (history, what each version changed, restore), `SaveVersionWindow`, `ChapterHistoryWindow`, `CompareWindow`, `GitHubWindow` | `App/*.cs`, `MainWindow.Versioning.cs` | the hand-test script in `docs/VERSIONS-AND-GITHUB.md` | [x] 2026-09-15 |
| 5.7 | It looks after itself: a version when the book is put down (per book, on by default), a version before every backup, and a push after every version when the book is connected; long jobs off the UI thread | `MainWindow.Versioning.cs` | closing a book with unsaved work leaves one version behind | [x] 2026-09-15 |
| 5.8 | Docs: `docs/VERSIONS-AND-GITHUB.md`, CLAUDE.md, README, manual-testing notes, and the privacy policy (the book can now leave the computer) | docs, `PRIVACY_POLICY.md` | the deployed privacy page needs the same change — **PunchMonkeyServer `PrivacyAuthorPlus.razor`, not done here** | [x] 2026-09-15 |
| 5.9 | **Ron's hand-test**, including the first real GitHub backup | | **Passed 2026-09-15** — Ron backed *The Book of One* up to the private `github.com/reaglin/The_book_of_one` and reports it works | [x] 2026-09-15 |
| 5.11 | **How to get a token**, in the window that asks for one: five numbered steps, the classic-token note, and what AuthorPlus does with it. Ron, 2026-09-15: *"because it uses GitHub tokens, it should give basic instructions on how to obtain a token in the process"* | `App/GitHubWindow.cs` | the steps read without leaving the window | [x] 2026-09-15 |
| 5.12 | **Back Up to GitHub no longer greys out** when a book keeps no versions. Ron, 2026-09-15: *"it is not obvious you need to enable saving versions"* — so the GitHub item (and Save a Version, and the history) stay live for any open book and start versioning themselves, with the question asked in GitHub's terms; the two everyday buttons that really do need a connected book say why in their tooltip | `MainWindow.Versioning.cs`, `MainWindow.xaml` | a first-time author reaches GitHub in one menu click | [x] 2026-09-15 |
| 5.10 | Open a book *from* GitHub on a second computer (clone inside the app) — deferred until two machines are actually in use | | | [ ] |

## Phase 6 — Editor modes: Write, Analysis, Characters (Ron, 2026-11-06)

Ron, 2026-11-06: *"The editor is going to have modes and some of the items on the top menu (editor menu) are going to
change. Editor mode is going to be just that, the editing tools. The next mode will Analysis mode … the AI to make
suggestions on a Chapter and Paragraph level … a colored Paragraph icon at the start of each paragraph. Clicking any
Paragraph icon will bring up the Analysis screen for that paragraph. The Chapter analysis will allow for the creation of
the Summary, Characters, Plotlines, and Chapter Analysis for the chapter. We will also be adding a Character mode that will
highlight all references to characters in a chapter and allow for easy access to the Character forms."*

Decided with Ron the same day (four questions, all answered with the recommended choice):
- the paragraph screen gives **a read of the paragraph and rewrites** (Apply / Mark / Ask again, the existing suggestion screens);
- the ¶ colour is **the paragraph's state**: grey not read, blue read, amber rewrites waiting, green dealt with — no AI cost until a paragraph is clicked;
- both Analysis screens open in **a panel beside the text**, which stays in view;
- in Character mode a name opens **that character's form beside the text**; capitalised names that are not characters yet are underlined with dots and can be added.

Design notes: the marks are drawn over the text (`ChapterOverlay`, an adorner), never into the chapter, so nothing they show
is saved with it; in Analysis and Character mode the text is read-only (applying a rewrite still changes it). A paragraph
read is an item (`ItemKind.ParagraphAnalysis`) owned by the chapter, holding the paragraph as it was read; it finds its
paragraph again by its text, or by most of its words if the author has changed it. Reads are not shown in the tree.
The top menu bar keeps its items (the AI menu still has every AI action); **View** gains the three modes.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 6.1 | **The modes.** A row of three buttons above the chapter — ✎ Write · ¶ Analysis · 👤 Characters — with a line saying what the mode is for; **View › Write / Analysis / Character Mode** (Ctrl+1/2/3). The mode stays as the author moves between chapters. **Write mode's toolbar is the editing tools only**: Summarize, Analyze…, Chapter Characters and Suggestions… left it (they are in Analysis mode and the AI menu) | `MainWindow.Modes.cs`, `MainWindow.xaml(.cs)` | switching keeps the place in the chapter; Write looks as before minus the four AI buttons | ⚠️ built 2026-11-06 — Ron to try |
| 6.2 | **Analysis mode — the chapter.** With no paragraph chosen, the panel shows the chapter's **Summary**, **Characters**, **Plotlines** and **Chapter analysis**, each with what there is and a button to make it (Write the summary, Find who is in it, Find its plotlines, Analyze the chapter…, One aspect ▾) and Open; a **Paragraphs** card counts what has been read and goes to the first paragraph not read or with rewrites waiting. A result made from here stays on the chapter instead of jumping to the new item in the tree | `MainWindow.Modes.cs` (`ChapterAnalysisPanel`, `ShowResult`) | each button runs the existing AI action; the card updates when it finishes | ⚠️ built 2026-11-06 — Ron to try on *The Book of One* |
| 6.3 | **Analysis mode — a paragraph.** A coloured ¶ at the start of every paragraph (three words or more, not scene breaks); clicking it opens the paragraph in the panel: its text, Previous/Next, "What should this paragraph do?" (optional), **Read this paragraph (AI)…** through the usual prompt console, then the read and its rewrites, **Dealt with** and **Delete this read**. The prompt `paragraph-read` sends the paragraph, three before, two after and the chapter summary — not the whole chapter. A rewrite marked for your own version appears in Book › Marked Passages too | `ChapterOverlay.cs`, `MainWindow.Modes.cs`, `AiPrompts.cs`, `Item.Passage`/`ParagraphIndex`, `SuggestionViews.cs` | tried 2026-11-06 on the sample book with Claude: one read, three rewrites, the ¶ amber; Apply changed the text, the ¶ turned green and the panel said "dealt with" | ⚠️ built 2026-11-06 — Ron to try |
| 6.4 | **Character mode.** Every character's name and "also called" washed in their own colour wherever the chapter uses it; possible new names dotted-underlined. The panel lists the chapter's cast (mentions, point of view, "not on the chapter's list" with a button to add them), who is listed but not named, and names that are not characters yet. A name opens **the character's form** (the existing character page) beside the text with Previous/Next mention; a new name offers **Add as a character** or **Add as another name for …** (a name that is part of a character's name, "Nell" for Nell Carrow, is offered for them first). **All characters** opens the Characters page | `MainWindow.Modes.cs`, `ChapterOverlay.cs` | on the sample book "father" is coloured as Tom Carrow (his "also called"), "Gull Point" is offered as a possible name | ⚠️ built 2026-11-06 — Ron to try |
| 6.5 | Core: `ParagraphReads` (the READ part of a reply; which paragraph a read belongs to now; the state behind the ¶ colour) and `NameScan` (where each character is named, longest name first; capitalised names no character answers to; names set aside in `Book.NotNames`, kept in book.json) | `Core/Services/ParagraphReads.cs`, `NameScan.cs`, `tests/…/EditorModesTests.cs` | 12 new tests; 160 in all, green | ⚠️ built 2026-11-06 |
| 6.6 | UX review of the three modes (cognitive walkthrough + heuristics, 2026-11-06): 4 severity-3, 8 severity-2, 3 severity-1 findings | | **Fixed:** an applied rewrite moves its read with it (no false "changed", no lost read; reads of vanished paragraphs listed from the Paragraphs card); the rewrite frames fit the panel; typing in a read-only mode says so, and a double-click goes back to Write with the caret there; **Undo apply** on an applied rewrite (disabled buttons say why); a newer read with rewrites waiting is no longer hidden by an older one dealt with; "The passage before your change"; clicking anywhere in a paragraph opens it, and "Open paragraph 1" is always offered; AI buttons grey out while a run goes; Write it again asks before replacing a summary; the current mention is underlined; a capitalised "also called" (Father) no longer matches "her father"; **Not a character — stop marking it** (kept with the book, can be undone); the pick-a-character box starts on the likely one; the place in the text, not the pixel, is kept between modes; colours by order of appearance in the chapter; dates on analysis links; one word per mode and per arrow. **Not done:** F1 help per screen (missing in the whole app; for the Help round) and keeping Undo history across a mode switch (the editor is rebuilt) | ⚠️ Ron to look at the visual points: the white ¶ on amber, the colour washes behind text, 150 % scaling |
| 6.8 | **Two kinds of paragraph analysis** (Ron, 2026-11-06: one is an analysis of the paragraph — its tone, consistency, style and what it achieves; the other is suggestions — possible changes, additions and suggestions to improve the readability). The paragraph panel has **Analyze this paragraph (AI)…** (prompt `paragraph-analysis`: TONE, CONSISTENCY, STYLE, WHAT IT ACHIEVES; no rewrites; the ¶ turns blue) and **Suggest changes (AI)…** (prompt `paragraph-suggestions`: 2–5 changes, additions and readability fixes, each tagged in its "why", each one you can apply; the ¶ turns amber). Each result is its own card, Analysis or Suggestions; reads made with the first build's single `paragraph-read` are still shown, as "Read". The ¶ words are now "not looked at yet / analysed / suggestions waiting / dealt with" | `AiPrompts.cs`, `MainWindow.Modes.cs` | tried 2026-11-06 on the sample book with Claude: the analysis came back under the four headings with the ¶ blue; four suggestions with the ¶ amber; 160 tests green | ⚠️ built 2026-11-06 — Ron to try (N3) |
| 6.7 | **Ron's hand-test** — `docs/MANUAL-TESTING.md`, round 2026-11-06 | `manual-test\AuthorPlus.exe` | | ⚠️ Ron |

## Phase 7 — Character analysis: in a chapter and across the book (Ron, 2026-11-06)

Ron, 2026-11-06: *"When we are dealing with a character in a Chapter, we want the chapter specific information. This will
come from an AI analysis of the Chapter and the characters role in the chapter and a summary of the character as they are
presented in the book. Role: The specific role in this chapter … If the character is introduced in a specific chapter this
will also be noted here. Description: Any specific description given in the chapter including any personality
characteristics from that specific chapter. Motivations and Actions: From this chapter any specific motivations and actions
they take including actions that affect the character. In addition to chapter analysis of characters, there is also a book
level analysis, which includes story arc (the progression of the character over the chapters), and plotline interactions.
The user should be able to open the book level character analysis from the chapter level."* More is to come: character
interactions and plotline interactions.

**How it is built.** A chapter analysis is an item of the chapter (`ItemKind.CharacterInChapter`, the character in
`CharacterIds`); a book analysis is an item of the character (`ItemKind.CharacterInBook`). Neither is in the tree. Each run is
kept; the latest is shown. The book analysis does not send the chapters' full text — it sends, for every chapter the
character is in, the chapter's summary and their analysis in that chapter, with the book's plotlines; so analysing more
chapters gives a fuller arc.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 7.1 | **In a chapter.** Characters mode: click a character's name — the panel has three views, **In this chapter · In the book · Their form**. *In this chapter*: **Analyze {name} in this chapter (AI)…** (prompt `character-in-chapter`: the chapter, what the author and the book analysis say about them, and where they first appear) writes **Role** (their part here, and "Introduced in this chapter" when it is), **Description** and **Motivations and actions**; under it, the book analysis's summary with **Open the book analysis** | `MainWindow.CharacterAnalysis.cs`, `MainWindow.Modes.cs`, `AiPrompts.cs` | tried 2026-11-06 on the sample book with Claude: Nell in "The Cold Wick" came back "Introduced in this chapter", with her description and actions quoted | ⚠️ built 2026-11-06 — Ron to try (R1–R3) |
| 7.2 | **In the book.** **Analyze across the book (AI)…** (prompt `character-in-book`) writes **Summary**, **Story arc** (chapter by chapter) and **Plotline interactions**; shown in the panel's *In the book* view and at the end of the character's page (a link at the top of the page goes to it), with **In each chapter** under it: every chapter they are in, its Role line, a click opens that chapter in Characters mode with them beside it. It says when chapter analyses are newer than the book analysis. Where there is nothing to build it from, it says what to do instead of offering the button | same | tried 2026-11-06 with Claude: summary and story arc for Nell | ⚠️ built 2026-11-06 — Ron to try (R4–R6) |
| 7.3 | Core: `CharacterAnalyses` — a reply split at its headings, the latest analysis in a chapter and in the book, the chapters a book analysis draws on, where the character first appears | `Core/Services/CharacterAnalyses.cs`, `tests/…/CharacterAnalysesTests.cs` | 7 new tests; 167 in all, green | ⚠️ built 2026-11-06 |
| 7.4 | UX review (walkthrough + heuristics, 2026-11-06): 1 severity-3, 4 severity-2, 4 severity-1 | | **Fixed:** the book analysis is not offered when there is nothing to build it from — it says what to do; book analyses kept out of the tree; the chapter chips on a character's page open the chapter with them beside it, like the new links (the heading is now "Chapters they are in"); a link at the top of the page to the analysis, and the page goes to it after a run; AI buttons on a page grey out while a run goes and come back after; **Fill in from the book (AI)** no longer greys out without a key (it offers AI Settings); the cast list's tooltips; one wording ("In the book", "analyze"); "newer chapter analyses" note; a different character opens on *In this chapter*. Also: links (`Palette.LinkText`) are real hyperlinks now — they take Tab and Enter and screen readers see them | ⚠️ Ron to look: the three view buttons (is the chosen one clear?), long buttons in the 430 px panel, the grey text on the cards |

## Phase 8 — Create and Plotlines modes (Ron, 2026-11-06)

Ron, 2026-11-06: *"We will be adding 2 modes to the top buttons, "Plotlines" and a "Create" mode after the write mode. For
chapters this will be the plotlines that are specifically in that chapter. Characters that progress through the book also have
associated plotlines, which are their story. Plotlines is similar to Character and Analysis modes, it has chapter and book level
information. The Create Mode is simple — It allows the user to specify what they want the AI to write and the AI writes the
text … The Create mode allows the user to add chapters, but also add paragraphs between [paragraphs] (using a + symbol between
paragraphs to do the insertion) including a + at the end. … Action: … Style: … The program should remember the different types
of styles so they can be reused … a link at the end of the text box to allow the user to choose from previously entered styles."*

The modes are now **✎ Write · ✚ Create · ¶ Analysis · 👤 Characters · 🧵 Plotlines** (Plotlines last — confirmed by Ron, 2026-11-06), Ctrl+1 to Ctrl+5 (Analysis moved from
Ctrl+2 to Ctrl+3, Characters from Ctrl+3 to Ctrl+4) and on the View menu.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 8.1 | **Create mode — new text at a +.** A green + in the margin above every paragraph and one after the last; click one (or ‹ Earlier place / Later place › in the panel) and say the **Action** (what should happen / what the reader should take from it) and the **Style** (how), and a length; **Write it (AI)…** (prompt `create-passage`: the paragraphs around the place and the chapter summary, in the voice of the text around it). The draft is shown beside the text to read and edit; **Put it in the chapter** puts it in with a green band (styled like the nearest ordinary paragraph, not a scene break); **Take it out again** until the chapter is left, also after a mode switch | `MainWindow.Create.cs`, `ChapterOverlay.cs`, `AiPrompts.cs` | tried 2026-11-06 on the sample book with Claude: a paragraph written at the end of chapter 1, put in, taken out; put in, Write mode and back, taken out — the word count back to 164 | ⚠️ built 2026-11-06 — Ron to try (S1–S4) |
| 8.2 | **Saved styles.** A style is kept when the AI writes with it (one list for every book, `Documents\AuthorPlus\writing-styles.json`, most recent first); **Choose a saved style ▾** under the style box puts one back; **Forget a style** takes one off | `Core/Services/WritingStyles.cs` | tests | ⚠️ built 2026-11-06 — Ron to try (S2) |
| 8.3 | **Create mode — a new chapter.** With no + chosen: Title, Action, Style, length; **Write the chapter (AI)…** (prompt `create-chapter`: the synopsis, this chapter's summary and ending). **Read it first** (Ron, 2026-11-06: "Add a preview step for new chapters"): the chapter is shown in the panel to read and edit — nothing is in the book yet — then **Add it to the book** puts it after this chapter and opens it; **Discard** (asks if edited) | `MainWindow.Create.cs` | tried 2026-11-06 with Claude: "The Morning After" (about 2,200 words) shown in the panel, then added as chapter 2 and opened | ⚠️ built 2026-11-06 — Ron to try (S5) |
| 8.4 | **Plotlines mode — in a chapter.** The panel lists the chapter's plotlines (its part here, analyzed or not, whose story); **Find its plotlines (AI)**, **Add a plotline ▾**. Click one: *In this chapter · In the book · Its page*. **Analyze it in this chapter (AI)…** (prompt `plotline-in-chapter`) writes its part here, what happens, characters and **key moments quoted from the text** — washed in the thread's colour in the chapter, with ‹ Previous / Next › and a line naming any quote not found | `MainWindow.Plotlines.cs`, `Core/Services/PlotlineAnalyses.cs` | tried 2026-11-06 with Claude on "Who put out the lamp": all 5 key moments marked (a quote with dialogue inside it found after a fix) | ⚠️ built 2026-11-06 — Ron to try (T1–T3) |
| 8.5 | **Plotlines mode — across the book.** **Analyze across the book (AI)…** (prompt `plotline-in-book`): summary, progression over the chapters, characters, where it meets other plotlines; built from the chapter summaries and the thread's chapter analyses; on the plotline's page too, with **In each chapter** | same | built; not yet run with the AI | ⚠️ built 2026-11-06 — Ron to try (T4) |
| 8.6 | **A character's story is a plotline.** `Plotline.CharacterId`. On a character's *In the book*: **Make {name}'s story a plotline** (linked to their chapters, its summary from their story arc); on a plotline's page (and in the panel): **Whose story is it?** — asks before moving the story from another thread, offers to link the character's chapters | `MainWindow.Plotlines.cs`, `Book.cs` | tests | ⚠️ built 2026-11-06 — Ron to try (T5) |
| 8.7 | Core: `PlotlineAnalyses` (latest per chapter and book, the chapter's threads, key moments as the ways to find them), `Book.AddToChapter`, `WritingStyles`, `CreatedText` (the AI's reply as paragraphs); the heading parser no longer takes "Characters gather…" as a heading | `Core/Services/*`, `tests/…/CreateAndPlotlinesTests.cs` | 11 new tests; 178 in all, green | ⚠️ built 2026-11-06 |
| 8.8 | UX review (walkthrough + heuristics, 2026-11-06): 1 severity-3, 7 severity-2, 4 severity-1 | | **Fixed:** Take it out again after a mode switch removed nothing (it finds the text again now); take-out kept until the chapter is left; "Just before / Just after" labels; no paragraph numbers in Create (they differed from Analysis); new text styled like an ordinary paragraph, not a scene break; ‹ Earlier place / Later place › and "At the start" for the keyboard; Whose story asks, links chapters and says what it did, and is in the panel's *Its page* too; the story's summary from the story arc; edited drafts are not thrown away without asking; styles kept only when the AI ran; "See {name} in this chapter"; the not-found line wraps; a clearer green band; "analyzed". A new chapter now has a read-first step too (8.3) | ⚠️ Ron to look: the + marks between close paragraphs, overlapping washes of two threads, the green band |

## Phase 9 — Voices: Listen mode and the audiobook (Ron, 2026-11-06)

Ron, 2026-11-06: *"This is the biggest addition to the book, it is voices. We plan to have the ability to create an audiobook
from the book and use Google TTS voices for the characters. To do this we need connectivity to Google TTS, the ability to assign
voices to speaking characters and the selection of a narrator voice. There will be other requirements as we progress, we will want
the ability to listen to a chapter, and to publish in different formats (audio and text)."* This brings forward
"Version 2 — Audio" below.

**Decided with Ron (2026-11-06):** Google is reached through **AI Manager 1.8.0** (its Phase 5), with a **Google API key first**
and a service-account file accepted too (CourseBuilder+'s can be imported); a **🔊 Listen mode** (the sixth, Ctrl+6); the **AI marks
who says each line, the author confirms** or changes it.

**How it is built.** `Book.NarratorVoice`, `Book.VoiceLanguage` and `Character.Voice` (`VoiceChoice`: a Google voice name and
pace). Who says each line of a chapter is an item of the chapter (`ItemKind.ChapterSpeakers`, its `Lines`), each line found again
by its words and which time they are said; the author's choice is never overwritten by the AI. A chapter is read as parts
(`Dialogue.Plan`): narration in the narrator's voice, each line in its speaker's (the narrator's when they have no voice). Every
spoken part is kept in the book's `audio\cache` folder under a key of voice, pace and words, so it is paid for once; `audio\` is
never versioned (the book's `.gitignore`, added to older books when they open) and never in an Export Project file.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 9.1 | **AI Manager 1.8.0 — text to speech** (in `..\AiManager`): the Google credential (API key or service account) in AI Settings with step-by-step help and Test; the voice list; speaking long text in pieces under Google's 5,000-byte limit; prices and the usage ledger; a voice chooser with **Hear it** | `..\AiManager` (its plan, Phase 5) | its tests; ⚠️ a real Google key | ⚠️ built 2026-11-06 (AiManager `7c3c33f`; 170 + 219 tests) — it found CourseBuilder+'s service account and imported it; 2,066 voices listed, one line spoken. Ron: make an **API key** (AI Settings ▸ Voices ▸ How?) and Test — that path has not met Google yet |
| 9.2 | **Voices.** The narrator's voice (Listen panel) and each character's (Listen panel and their page): **Choose…** opens the voice chooser — language, kind of voice and its price, Hear it; **Use the narrator's** | `MainWindow.Listen.cs`, `Book.cs`, `BookStore.cs` | tests (round trip) | ⚠️ built 2026-11-06 — Ron to try (V1–V2) |
| 9.3 | **Who speaks.** Every line of dialogue (in double quotation marks; a speech left open runs to the end of its paragraph) coloured by its speaker in Listen mode, dotted when not known; **Find who speaks (AI)…** (prompt `dialogue-speakers`: the chapter with each line numbered, the cast); click a line: **Who says it?** — a character, the narrator, or someone new (added as a character); ‹ › and **Next not known** | `Core/Services/Dialogue.cs`, `MainWindow.Listen.cs` | 9 new tests; 187 in all, green | ⚠️ built 2026-11-06 — Ron to try (V3–V4) |
| 9.4 | **Listen.** **▶ Play** from the clicked paragraph (or the start), **■ Stop**; the piece being read highlighted and followed; the next piece fetched while one plays; what it will cost before playing (characters not heard before × the voice's price) and what was spent | `MainWindow.Listen.cs`, `ChapterOverlay.cs` | tried 2026-11-06 on the sample book: the AI marked both of Tom's lines; played with Charon (narrator) and Algenib (Tom), the paragraph being read highlighted, about $0.01; afterwards only the parts not yet heard were counted | ⚠️ built 2026-11-06 — Ron to try (V5–V6) |
| 9.5 | UX review of Listen mode (walkthrough + heuristics, 2026-11-06): 3 severity-3, 6 severity-2, 3 severity-1 | | **Fixed:** Stop no longer rebuilt under the mouse while reading (only the status line changes), Space plays/stops and Esc stops; a new character takes every line the AI gave that name; a speaker stays with their line when the same words are added earlier (the words around a line are kept with it); a correction stays on its line; ▶ in the margin where Play starts, and a click while reading reads from there; paragraph numbers count only paragraphs that are read; the cost is asked once a session above 25 cents or when a price is not known; "Getting the voices from Google…"; Stop on a line's panel; a chapter with no dialogue says so (and that only double quotation marks count); a damaged kept piece is made again; leaving says it stopped; a pace for each voice; "Others in the book". Found in the running app: the voice rows were squeezed — two lines now | ⚠️ Ron to look: the colours of speakers and the narrator's grey, the highlight while reading |
| 9.6 | **The audiobook** — planned as phase 10 (Ron, 2026-11-06) | | | → phase 10 |
| 9.7 | **Publish in different formats** (audio and text): the text exports (DOCX, EPUB, PDF, Markdown) and the audiobook from one place | | | → phase 10 (10.10) |
| 9.8 | Single quotation marks for dialogue (‘…’, as many British books use) — told apart from apostrophes | `Dialogue.cs` | | to do |
| 9.9 | Pronunciation of names | | | → phase 10 (a list per book, Ron 2026-11-06) |

## Phase 10 — The audiobook and name pronunciations (Ron, 2026-11-06)

Ron, 2026-11-06 (after hearing a chapter read by the narrator alone: *"the read was very good"*; built the same day — *"Go ahead and build phase 10"*): *"Let's plan the audiobook
export next. We will want to be able to create by Chapter and put them together into a single file. We will need a strategy
for name pronunciations."*

**Decided with Ron (2026-11-06):**
1. **Per chapter, then one file:** an MP3 for every chapter (what the stores take), and **one M4B** with chapter marks (the
   audiobook format: Apple Books and most audiobook players jump between chapters). Made with **Windows' own encoder** (Media
   Foundation, through NAudio, MIT) — nothing else to install, nothing GPL shipped.
2. **Names: a list kept with the book, drafted by the AI.** Each name, place or invented word with **"say it as"** — a plain
   respelling (*oh-leh-meh-LOO-kway*) that every voice can read — and, where the voice takes it, **exact phonetics** (IPA;
   Google's custom pronunciations, Chirp 3: HD in English (US) today). The AI proposes entries for every character, also-called
   and place; the author **hears each one** in the narrator's voice and adjusts. The same list is used by Listen mode.
3. **The store standard:** 192 kbps MP3 at 44.1 kHz, steady loudness, a short silence at the head and tail of each chapter,
   opening and closing credits that say the narration is a **synthesized voice** (Google Play Books, Kobo and Apple Books
   accept synthesized narration when it is declared; Audible/ACX does not take it from outside — the export says so).

**What it costs and how long it is (the trilogy):** 162,878 words ≈ 950,000 characters ≈ 18 hours of audio. In Chirp 3: HD at
Google's $30 per million characters that is about $28, and Google's first million characters each month are free — so the whole
trilogy may cost little or nothing the first time. What has been heard in Listen mode is already paid for and reused.

**How it will be built.** The chapter is read exactly as Listen mode reads it (the narrator, each character's voice, the
speakers the author confirmed, the pronunciation list), but asked of Google as **WAV at 44,100 Hz** so it is encoded once, at
the store's quality. Each chapter's audio is kept in the book's `audio\chapters\` (never versioned or exported, like the cache)
with a note of the words it was made from, so **only chapters whose text, speakers, voices or pronunciations changed are made
again**. Joining, silences, loudness and the M4B's chapter marks are worked out in Core (tested); the encoding is in the App
(Windows only). A long export runs in the background with progress, can be stopped and picked up again, and says what it will
cost before it starts.

| # | Task | Targets | Done when | Status |
|---|---|---|---|---|
| 10.1 | **AI Manager 1.9.0:** `AiSpeechRequest.SampleRateHertz` (WAV at 44,100 Hz) and `Pronunciations` (phrase → IPA, sent as Google's custom pronunciations where the voice takes them — Chirp 3: HD in en-US — and retried once without them if Google refuses one); `AiVoice.AcceptsPronunciations`; `AiSpeech.PronunciationsIgnored` | `..\AiManager` (`5426298`) | 14 new tests (184 + 233); a real call: "Dalgo met Olemelukwe." with the IPA, 44.1 kHz WAV, accepted | ⚠️ built 2026-11-06 — Ron to hear a listed name |
| 10.2 | **Pronunciations list** (Core): `Book.Pronunciations` (word, say it as, IPA, note, from the AI) applied to the words sent to Google — whole words ("Dalgo's" too), the longest first, case-sensitive for a capitalised entry, IPA where the voice takes it, the respelling (in lower case, so capitals are not spelled out) otherwise; part of the audio key, so a changed pronunciation is heard again; used by Listen mode too | `Core/Services/Pronunciations.cs` | tests | ⚠️ built 2026-11-06 |
| 10.3 | **Pronunciations page** (a node in the tree, and its right-click): the list with **▶ Hear** in the narrator's voice, **✓** (it sounds right — no longer the AI's), **Remove** (asks), **+ Add a word** (the cursor goes to it), **Suggest from the book (AI)…** (prompt `pronunciations`: the cast, also-calleds, places and capitalised words, 300 at a time; words asked about are remembered so the next run asks about the next ones); a count of the AI's entries not yet checked | `MainWindow.Audiobook.cs` | tried 2026-11-06 with Claude on the sample book: Carrow, Silas, Ames, harbourmaster suggested with respelling, IPA and a note (a little eager with common names) | ⚠️ built 2026-11-06 — Ron to try (W1–W3) |
| 10.4 | **A chapter's audio:** read as Listen reads it, each piece asked of Google as 44.1 kHz WAV (three at a time) and kept as it arrives (so Stop loses nothing paid for), joined with pauses (¼ s in a paragraph, ⅔ s between, 1.6 s at a scene break, 1.3 s after the title; the part's title before its first chapter), 1 s of silence at the head and 3 s at the tail, loudness evened to −20 dB RMS with peaks held under −3.8 dB, 192 kbps MP3 (two channels — Windows' MP3 encoder stops at 128 kbps for one) | `Core/Services/Audio/Pcm.cs`, `MainWindow.Audiobook.cs` | tests; the sample book's tracks measured with ffmpeg: −21.5 to −22.9 dB RMS, peaks −3.7 dB or lower, 1.0 s head, 3.0 s tail, 192 kbps 44.1 kHz | ⚠️ built 2026-11-06 |
| 10.5 | **File ▸ Export ▸ Audiobook…:** chapters to tick (All, None, a part only), what each needs (made, not made, or why it changed: its number or title, its text or speakers, a voice, a pronunciation), the cost and the time to make before starting — and asked when it is more than 25 cents; progress per piece; Stop and carry on another day; only what changed is made again | `MainWindow.Audiobook.cs` | tried 2026-11-06 on the sample book: 8 tracks, 4:04, about $0.09; again — nothing to make; Part Two only — its own folder | ⚠️ built 2026-11-06 — Ron to try (W4–W7) |
| 10.6 | **Credits:** an opening and a closing track in the narrator's voice saying the narration is a synthesized voice; the wording editable in the window, kept with the book; made again only when they change | `MainWindow.Audiobook.cs` | | ⚠️ built 2026-11-06 |
| 10.7 | **One M4B:** all the tracks in one AAC file (64 kbps, one channel — about 500 MB for the trilogy) with chapter marks in both forms players read (a QuickTime chapter track and a Nero list) and the book's details (title, author, narrator "Synthesized voice", year, marked as an audiobook) and the cover (`cover.jpg` or `cover.png` in the book folder, if there is one) | `Core/Services/Audio/Mp4Book.cs` | tests (sound where its index says, both layouts); ffprobe on a real file: every chapter mark, the title, author, narrator, media type audiobook; decodes cleanly | ⚠️ built 2026-11-06 — Ron to open the M4B in a player |
| 10.8 | **The output folder:** `Documents\AuthorPlus\Audiobooks\{Title}\` (remembered if changed; a selection of chapters gets its own folder — "{Title} — Part Two" — so it never mixes with the whole book; old files are replaced only after asking): `00 Opening credits.mp3`, `01 …`, `99 Closing credits.mp3`, `{Title}.m4b`, `chapters.txt` (times), `Where to publish.txt` (what to upload to Google Play Books, Kobo and Apple Books; not Audible/ACX) | `MainWindow.Audiobook.cs` | | ⚠️ built 2026-11-06 |
| 10.9 | UX review (walkthrough + heuristics, 2026-11-06): 1 severity-4, 5 severity-3, 6 severity-2 | | **Fixed:** a part's export no longer mixes with the whole book's (its own folder; replacing asks); the cost and time are confirmed before paying; Stop keeps the pieces of the chapter being made (and says the M4B step cannot be stopped part way); "{Part} only" buttons; ✓ to accept the AI's pronunciations and a count of those not checked; the AI asks about the next 300 words each time; why a chapter is to be made again; the window works out every chapter once (it stays alive); credits not made again when unchanged; Hear says what it is doing; Remove asks; the new word gets the cursor; which files go to which store, and whether there is a cover; the Save in folder is remembered; the disabled button says why. Ron's hand-test with a part of the trilogy is next | ⚠️ Ron: the export window on a 1366 × 768 screen; long IPA in its column |
| 10.11 | **Approve a chapter in Listen mode** (Ron, 2026-11-06: *"what will often be the case is the author listens to a chapter and then approves that chapter for the audiobook"*): the Listen panel's **Audiobook** card says where the chapter stands — not in the audiobook yet, made but not approved, approved (with the date and length), or out of date and why; **✓ Approve for the audiobook…** makes its track now at the audiobook's quality (the cost first, asked above 25 cents; Stop keeps the pieces made) and marks it approved; **▶ Hear the track**, **Save the MP3…** (one chapter, to send or upload), **Withdraw approval**. The export says "approved" beside those chapters and has **Approved only**; an approved chapter whose text, speakers, voices or pronunciations change says so | `MainWindow.Listen.cs`, `MainWindow.Audiobook.cs` (the track making shared by both) | tried 2026-11-06 on the sample book: chapter 1 approved (its track made, 1:01, about $0.03); the export shows "approved, 0:01:01" | ⚠️ built 2026-11-06 — Ron to try (W8) |
| 10.12 | **Examples for the next publish** (Ron, 2026-11-06: *"Keep your real test exports and stories, they can publish as examples with the next publish"*): the sample book *The Keeper of Gull Point* (now in `Documents\AuthorPlus\Books`, with its approved and made tracks) and its audiobook exports (`Documents\AuthorPlus\Audiobooks\The Keeper of Gull Point` and `… — Part Two — The Ledger`: numbered MP3s, the M4B with chapter marks) go on softwareplus.ai/author as examples — hear a chapter, download the M4B — with the next release | SoftwarePlus `site/author/` | | to do with the next release |
| 10.10 | **Publish (audio and text) from one place:** one window for the book's exports — DOCX, EPUB, PDF, Markdown and the audiobook — with what each is for | `App` | | later |

## Release 1.1.0 — voices, the audiobook and the editor's modes (Ron, 2026-11-06: "Let's prepare a software release")

Phases 6–10 go to the Store as 1.1.0. The steps and the text to paste are in `docs/STORE-SUBMISSION.md`, "Version 1.1.0".

| # | Task | Done when | Status |
|---|---|---|---|
| R11.1 | AI Manager 1.9.0 (the latest); version 1.1.0; Release build clean; 198 tests | | ⚠️ done 2026-11-06 |
| R11.2 | The submission package `artifacts\AuthorPlus_1.1.0.0_x64_bundle.msixupload` (80.4 MB) with the real identity; family name checked; NAudio and AI Manager inside | Ron uploads it first (his request) | ⚠️ built 2026-11-06 — Ron to upload |
| R11.3 | WACK on the 1.1.0 bundle | PASS | ⚠️ PASS 2026-11-06 (overall), on 1.1.0.0 |
| R11.4 | Privacy policy covers the voices (Google Cloud Text-to-Speech: what is sent, where, the audio kept in the book folder) | deployed | ⚠️ live 2026-11-06 |
| R11.5 | Listing: What's new, the description's two new parts, four features, six screenshots (`store\screenshots-1.1`), the certification note on the voices | pasted in Partner Center | ⚠️ done 2026-11-06; **submitted for certification 2026-11-06** (Ron: "you can complete the remainder of the publish") — publishes as soon as it passes |
| R11.6 | Ron's hand-test of the rounds M–W (`manual-test\AuthorPlus.exe`) before submitting | | ⚠️ Ron |
| R11.7 | After it is live: SoftwarePlus.ai's Author+ page (the modes, voices, audiobook; the examples of 10.12), the home tile's version, tag `v1.1.0` | | after |

## Every release — AI Manager and the manual-test build (Ron, 2026-10-11)

Ron: *"the new releases will need to update to the latest version and this will be something that
must occur with every version that we release of all the programs that use the AI Manager."*
These two items are done **again for every release**; a release is not ready for the Store until both are.

| # | Task | Done when |
|---|---|---|
| R.1 | **Move to the latest AI Manager** (`Eaglin.AiManager` and its UI package in the csproj). Read AI Manager's `docs/DEVELOPMENT-PLAN.md` for what changed; fix anything the new version changes; tests green | the csproj names the latest version in `C:\nuget-local`; tests green — **1.0.0: moved 1.2.0 → 1.7.0 on 2026-11-05** (additive changes only: How? and the key help page, speech to text, pictures); builds clean, 148 tests green |
| R.2 | **Hand-test build** in `manual-test\AuthorPlus.exe` (`docs/MANUAL-TESTING.md`); Ron opens AI Settings there and checks How? beside his provider | ⚠️ 1.0.0 built 2026-11-05 (30.2 MB) — Ron runs the round 2026-11 script |

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
- **2026-09-14 — EPUB and PDF export (4.1).** Two more entries on File › Export. The EPUB is
  written by hand as a zip, the same way the DOCX is: `mimetype` stored first, one XHTML file
  per chapter, a title page, a page per part, `style.css`, and both tables of contents. The PDF
  is typeset here rather than handed to a report library — words are measured, lines filled and
  spaced out to the margin, paragraphs indented, widows and orphans moved — over PDFsharp (MIT),
  with fonts read off the machine by `BookFontResolver` so Core keeps no UI dependency. Georgia
  is used when it is installed, then Constantia, Times New Roman, DejaVu Serif, Liberation Serif.
  Chosen before the export in a small setup window: 6 × 9 in book (the default), Letter or A4;
  straight or ragged right margin; page numbers on or off.
  Checked against the real trilogy (73 chapters, 162,886 words): EPUB 458 KB in 57 ms, PDF
  **647 pages, 1.8 MB, in 0.9 s**. `epubcheck` 5.1.0 reports **0 errors and 0 warnings** on it
  and on two edge-case books (a part plus a chapter outside it; no parts at all, with an empty
  chapter). 115 Core tests green — the new ones read the EPUB back as XML and the PDF back with
  PdfPig (page text, bookmarks nested under their part, every glyph inside the margins, lines
  reaching the right margin, page size per setup). Words carry their own trailing space so text
  copies out of the PDF with the spaces the manuscript has.
  **Ron:** worth opening the PDF in a reader and the EPUB on a device — the typesetting is the
  part a hand-test judges better than a test does. Export is still synchronous (a wait cursor and
  a status line); at a second a book that is fine, but if you export on a slower machine and it
  feels stuck, say so and it moves to a background thread.
- **2026-09-15 — Phase 5 built (versions and GitHub).** A book folder can now be its own git
  repository, with none of git's vocabulary on screen: Versions › Save a Version… (the name is
  offered from what changed), Version History… (what each version changed, in chapter and
  character names), This Chapter's History…, and the compare view that reads two drafts of a
  chapter as prose with the moved words marked. Putting the book back to a version is itself a
  new version, and anything unsaved is versioned first, so nothing is ever lost. GitHub: one
  token, signed in once and kept DPAPI-encrypted in `Documents\AuthorPlus\github.json`; the
  repository is created empty and private; backups happen after every version and when the book
  is closed; a clash between two computers leaves the folder untouched and asks which copy to
  keep. 137 Core tests green (22 new, including GitHub's error handling against a stub
  transport). Measured on a copy of the trilogy: first version of all 73 chapters and 162,886
  words **1.8 s**, reading what changed 132 ms, saving a version 286 ms, a restore 181 ms, and
  the whole history in **0.6 MB** of `.git`. libgit2's native DLL was checked in both shipping
  shapes — the single-file hand-test exe and the self-contained Store publish.
  **Ron:** the GitHub half has never talked to GitHub — sign in, create the repository for
  *The Book of One* and press Back up now; that is hand-test 6 in `docs/VERSIONS-AND-GITHUB.md`.
  Also: the privacy policy in this repo now says the book can leave the computer when you back it
  up — **the deployed page at `/privacy/authorplus` (PunchMonkeyServer) still says it does not**.
