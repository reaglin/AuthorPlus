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
| 4.1 | EPUB and PDF export | `Core/Services/Export` (PDFsharp, MIT) | validates in an EPUB checker | [ ] |
| 4.2 | Packaging: `AppxManifest.xml`, `pack.ps1` (publish → makepri → makeappx → signtool), `make-msixupload.ps1` (bundle + symbols, unsigned as the Store wants, with a package-family-name check), `run-wack.ps1`, version from `Directory.Build.props`; `app.manifest` for PerMonitorV2 | `packaging/` | a 71.8 MB `.msixupload` builds end to end | [x] 2026-09-14 |
| 4.3 | Privacy policy: `PRIVACY_POLICY.md` here, `PrivacyAuthorPlus.razor` in the PunchMonkeyServer repo at `/privacy/authorplus`, listed on `/privacy` | | committed; deploy with that repo's `deploy.ps1` | [x] 2026-09-14 |
| 4.4 | Store art: `make-store-assets.ps1` draws the mark once and emits every tile at five scales, the taskbar target sizes, the five Partner Center listing images and `AuthorPlus.ico` — 52 assets, none over WACK's 204,800-byte cap | `packaging/Assets`, `store/images` | contact sheet reads at 16 px | [x] 2026-09-14 |
| 4.5 | The submission guide: identity, the order to do everything in, Partner Center field by field, age-rating answers, certification notes, listing copy ready to paste | `docs/STORE-SUBMISSION.md` | | [x] 2026-09-14 |
| 4.6 | Screenshots (≥ 1366×768, four or more) | `store/screenshots/` | | [ ] |
| 4.7 | Deploy the privacy page; copy the package identity name out of Partner Center; set 1.0.0; build with the real identity; run WACK; submit | | Store listing live | [ ] |

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
