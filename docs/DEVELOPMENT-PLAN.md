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
| 1.10 | **Sections.** `Section { Id, Title, Summary, Notes, ChapterIds }`; `Book.Sections` + `SectionOrder`; chapters may still sit directly under the book. `BookStore`: `sections/{id}.json`, `FormatVersion` 2, version-1 books load with chapters at book level | `Core/Models`, `Core/Services/BookStore.cs` | round-trip, ordering, migration — unit-tested | [ ] |
| 1.11 | **Items.** `Item { Id, OwnerId, Kind (Summary, Analysis, CharactersInChapter, Notes), Title, Body, CreatedUtc, ModifiedUtc, Provider, Model, PromptName, CharacterIds, PovCharacterId }` in `items/{id}.json`; owners hold `ItemIds`; Summary is one-per-chapter and replaces `Chapter.Summary` (migrated into an item) | same | round-trip; owner delete removes its items; migration test | [ ] |
| 1.12 | **DOCX reader** in Core: `System.IO.Compression` + `XmlReader` → paragraphs, heading styles, bold/italic runs → FlowDocument XAML + word count. No NuGet package | `Core/Services/Import/DocxReader.cs` | fixture docx (built in the test) round-trips text, heading, bold, italic | [ ] |
| 1.13 | **Manuscript import**: a folder of `Chapter N - Title.docx` → chapters in a chosen or new section; N numeric or spelled out (`Twenty-Eight`, `14-`); subfolders ignored; preview list (number, title, words) before import; summary reports gaps (e.g. missing 23) and heading/file-name mismatches; originals untouched | `Core/Services/Import/ManuscriptImporter.cs`, `App/ImportWindow` | file-name parser unit-tested on all 73 real names; import of the three parts matches `TRILOGY-TEST-CASE.md` | [ ] |
| 1.14 | **Tree rebuild** (CIATLE `NavigationTree` style): Book root → Sections → Chapters → Items, then Characters, Timeline, Plotlines; glyph per node type; context menus (+ Section, + Chapter, + Item ▸ kind, Rename, Move up/down, Delete with confirm); item editors per kind; Section editor (title, summary, notes) | `App/MainWindow.*`, new `App/Views/*` | hand-test script in `TRILOGY-TEST-CASE.md` steps 1–2, 5 | [ ] |
| 1.15 | **Adopt `Eaglin.AiManager`** (AiManager Phase 3): package reference from `C:\nuget-local`; delete `src/AuthorPlus.AI` + `AiTests.cs`; AI Settings / Prompt Library / Dashboard menu items open the package windows; register the app's templates | `App/`, `AuthorPlus.sln` | AI menu works with keys entered only in AiManagerApp; tests green | [x] 2026-09-12 |
| 1.16 | **AI actions that produce items**: Summary (creates/refreshes the singleton), Analysis (new dated item per run; pick one provider or "every provider with a key", streamed in `AiRunPanel` with Stop), Characters in this chapter (AI proposal merged with a name scan against the Characters list; user confirms) | `App/AiActions.cs`, templates | step 3–4 of the hand-test script | [ ] |
| 1.17 | **Section-level items**: Outline (Ron's numbered chapter-flow style) and Section summary built from the chapter summaries; continuity prompt takes earlier sections' summaries as context | templates, `App` | Part 2 outline resembles `Chapter Summary - Power of 2.txt` in shape | [ ] |
| 1.18 | **Load the trilogy** and record Ron's notes: three imports, counts checked, chapter-23 question answered, first-impression notes appended below | | notes dated in `TRILOGY-TEST-CASE.md` | [ ] |
| 1.19 | **Add a chapter written elsewhere** (Ron, 2026-09-12: authors draft upcoming chapters or fill gaps on whatever device is handy). Context menu on a section or chapter: "Insert chapter from file…" — one DOCX, TXT or Markdown file → a new chapter at a chosen position (before/after the selected chapter, or by its number), the rest renumbered; the missing chapter 23 of *The Soul of Three* is the first real case | `Core/Services/Import/*`, `App` | insert at position round-trips; word count and title set; originals untouched | [ ] |

### 1B — Manuscript polish

Exit: a novelist could draft a whole book here and not miss Word for the drafting stage.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 1.1 | Autosave (idle timer + on focus loss) and a visible "saved" indicator | `MainWindow` | pull the plug mid-sentence; lose at most a few seconds | [ ] |
| 1.2 | Formatting toolbar: headings (chapter title / scene break), font size, lists, undo/redo, find & replace | chapter editor | | [ ] |
| 1.3 | Scene breaks within a chapter (`* * *`) and a scene list in the tree under each chapter | Models: `Scene`? — decide: keep prose flat, detect `* * *` | | [ ] |
| 1.4 | Drag-and-drop reorder in the tree; multi-select delete | `MainWindow` | | [ ] |
| 1.5 | Word-count goals: per chapter and per book, daily progress (words added today) | `Book.TargetWords`, a `progress.json` in the book folder | status bar shows today / total / target | [ ] |
| 1.6 | Export: Markdown and DOCX (whole book, in chapter order, with a title page) | `Core/Services/Export/*` | opens in Word with headings | [ ] |
| 1.7 | ~~Import a manuscript: DOCX or Markdown → chapters by heading~~ superseded by 1.12/1.13 (per-chapter files); the "one compiled DOCX split on headings" case stays here as a later add-on | `Core/Services/Import/*` | | [ ] |
| 1.8 | Book properties page (title, author, synopsis, genre, target) as a tree root item instead of a dialog | | | [ ] |
| 1.9 | Distraction-free / full-screen writing mode | `MainWindow` | F11 | [ ] |

## Phase 2 — Story bible

Exit: characters, timeline and plotlines are linked to chapters and to each other, and the app
can show what is inconsistent.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 2.1 | Cross-links UI: chapter ↔ POV character / characters present / plotlines; event ↔ chapters & characters | field forms → pickers | | [ ] |
| 2.2 | Character mentions: scan chapter text for character names, offer to link; "appears in" list on the character | `Core/Services/MentionFinder` | unit-tested on aliases | [ ] |
| 2.3 | Timeline view: ordered table with When / Title / chapters / characters; reorder; "chapter order vs story order" side-by-side | new `TimelineView` | | [ ] |
| 2.4 | Plotline board: per plotline the chapters it runs through; convergence markers; unresolved plotlines flagged | new `PlotlineView` | | [ ] |
| 2.5 | Consistency checks (rule-based, no AI): character in a chapter before introduced; event referencing a deleted chapter; plotline never resolved | `Core/Services/Consistency` | tests per rule | [ ] |
| 2.6 | Notes / research items as a fifth tree section (free-form, attachable to anything) | Models | | [ ] |

## Phase 3 — AI assistance

Exit: the AI features are the reason to buy the app, and every one is transparent about what it
sent and what it cost.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 3.1 | Prompt library: editable prompt templates (summarize, profile, continuity, style) with a preview of the exact payload before sending | `AI/Prompts`, a Prompt window | | [ ] |
| 3.2 | Continuity check: given selected chapters + character/timeline facts, list contradictions with chapter references | | | [ ] |
| 3.3 | Style analysis: sentence-length, passive voice, adverb density (local, no AI) + AI voice/tone read on request | `Core/Services/Style` local metrics tested | | [ ] |
| 3.4 | Plot analysis: acts / tension map from summaries; suggested convergences | | | [ ] |
| 3.5 | Streaming responses with cancel; per-call cost shown from usage; monthly spend total in AI Settings | `IAiProvider` gains a streaming overload | | [ ] |
| 3.6 | Long-chapter handling: chunking with a running summary when text exceeds the model's practical input | | never truncates silently | [ ] |

## Phase 4 — Export, packaging, Store

Exit: signed MSIX installs on a clean Windows 11 VM; Store listing submitted.

| # | Task | Targets | Acceptance | Status |
|---|---|---|---|---|
| 4.1 | EPUB and PDF export | `Core/Services/Export` (PDFsharp, MIT) | validates in an EPUB checker | [ ] |
| 4.2 | Packaging: `packaging/AppxManifest.xml`, `pack.ps1` (publish → makeappx → signtool), version from `Directory.Build.props` | `packaging/` | installs on a clean VM | [ ] |
| 4.3 | Privacy policy page on PunchMonkeyServer (`/privacy/authorplus`) + `PRIVACY_POLICY.txt` | | | [ ] |
| 4.4 | Store listing assets, screenshots, description; age rating; price | `store/` | | [ ] |
| 4.5 | Store name reservation (Ron) and first submission | | | [ ] |

## Later (captured, not scheduled)

- **Narrations** — reuse PreseMaker's Google TTS pipeline for chapter read-throughs; per-character
  voices for dialogue (needs speaker attribution in the text).
- Multiple books open at once; a "series" folder with shared characters.
- Collaboration / cloud sync (folder-of-files makes OneDrive/Dropbox work today; conflict handling would be the feature).
- Localisation.

---

## Notes from hand-testing

*(append dated notes here; they feed the next phase)*
