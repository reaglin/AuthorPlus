# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**AuthorPlus** — an AI-enhanced authoring tool for writing and organising a book. A tree view
(the same idea as CIATLE-FCE's program tree) holds the book's parts: **Chapters** (the
manuscript, in a rich-text editor), **Characters**, a linear **Timeline** of story events, and
**Plotlines** (with convergences). AI features draft summaries, character profiles, and later
continuity and style analysis. WPF on .NET 10, Windows 10/11, sold on the **Microsoft Store**.

Ron's description (Overall_To_Do, 2026-09-04): *"help a book author manage and track the
writing process"* — chapters first, then characters (beyond basics: motivations, actions),
a timeline that stays consistent even when chapters do not follow it, and plotlines that
converge. Under consideration: export in different formats, narrations (incl. per-character
voices), plot and writing-style analyses.

### Read these first

| File | What it is |
|---|---|
| `docs/DEVELOPMENT-PLAN.md` | **The work breakdown** — numbered tasks per phase with status. Start here. |
| `docs/TRILOGY-TEST-CASE.md` | Ron's real trilogy (73 DOCX chapters on OneDrive) — the live test case every feature is checked against. |
| `..\AiManager\docs\PLAN.md` | The shared AI layer this app consumes (package `Eaglin.AiManager`). |
| `README.md` | What the app is, for a reader who is not building it. |

Decisions taken with Ron on 2026-09-04: WPF/.NET 10 (not WinForms); **folder per book, one
file per item**; paid Microsoft Store app.

Decisions taken with Ron on 2026-09-12 (re-plan around real use):
- **Tree like CIATLE's Program Assessment:** Book → Sections (parts) → Chapters → Items.
  A chapter opens in the editor when clicked; its child *items* are per-chapter records —
  first kinds: **Summary** (AI-drafted, editable, one per chapter), **Analysis** (AI
  evaluation, kept as dated history with provider/model/prompt), **Characters in this
  chapter**, **Notes**. Items can also hang off a section or the book.
- **One book, three sections** for the trilogy; characters, timeline and plotlines are shared
  across the whole book.
- **Import** reads the per-chapter DOCX files (copied in; originals untouched) with a small
  DOCX reader in Core — no NuGet dependency for it.
- **AI comes from the shared `Eaglin.AiManager` package** (repo `..\AiManager`, built first).
  `src/AuthorPlus.AI` is the seed of that library and is deleted once the package is adopted
  (task 1.15). Several providers at once: an analysis can be run on every provider that has a
  key and compared. Until 1.15 lands, the copied layer below still stands.

## Solution structure

```
AuthorPlus.sln
├── src/AuthorPlus.Core/   net10.0     Models (Book, Chapter, Character, TimelineEvent, Plotline)
│                                      + Services/BookStore (folder-per-book persistence)
├── src/AuthorPlus.AI/     net10.0     IAiProvider + Claude/Gemini/OpenAI/Mistral providers,
│                                      AiSettings + AiSettingsStore (DPAPI), PreseMakerCredentials,
│                                      ActivityLog — no dependency on Core
├── src/AuthorPlus.App/    net10.0-windows  WPF: MainWindow (tree + editor), AiSettingsWindow,
│                                      PromptWindow. Assembly name AuthorPlus.exe
└── tests/AuthorPlus.Tests/ net10.0    xUnit — BookStore, AiSettingsStore, router, PreseMaker keys
```

`Directory.Build.props` sets the version (`AuthorPlusVersion`), nullable, implicit usings and
**TreatWarningsAsErrors** for every project — fix warnings, do not suppress them.

## Build, test, run

```powershell
dotnet build AuthorPlus.sln
dotnet test tests/AuthorPlus.Tests
dotnet run --project src/AuthorPlus.App
```

.NET SDK 10.0.400 is installed; `net10.0-windows` WPF builds from the CLI with no extra
workload. UI is hand-tested; everything in Core and AI has unit tests and must keep them.

## The four ideas that carry the design

1. **A book is a folder; every item is a file.** `Documents\AuthorPlus\Books\{Title}\` holds
   `book.json` (metadata + the id order of each collection), `sections/{id}.json` (a part;
   holds its chapter id order), `chapters/{id}.json` + `chapters/{id}.xaml` (the prose, a WPF
   FlowDocument), `items/{id}.json` (a per-chapter/section/book item with `OwnerId` and
   `Kind`), `characters/{id}.json`, `timeline/{id}.json`, `plotlines/{id}.json`. `BookStore`
   writes temp-then-move, deletes files for removed items on save, and loads a corrupt item
   file as "that item is missing", never "the book is unreadable". Do not introduce a database
   or a single-file package; export formats are exports. Sections and items arrive with
   `FormatVersion` 2 (tasks 1.10, 1.11); version-1 books load with their chapters at book level.
2. **Chapter prose stays out of memory until opened.** `Chapter` carries metadata and a word
   count; `BookStore.LoadChapterBody/SaveChapterBody` move the XAML. The editor saves the body
   when the selection leaves the chapter and on Save.
3. **AI goes through the shared AI Manager.** Target (task 1.15): `AiHub.Open("AuthorPlus")`
   from the `Eaglin.AiManager` package; requests name a provider or use the app default; prompts
   are registered templates (`chapter-summary`, `chapter-analysis`, `characters-in-chapter`,
   `character-profile`) the user can edit in the package's Prompt Library window; keys, log,
   usage and the dashboard live in `Documents\AiManager\`. *Until then* the copied layer
   applies: `AiProviderRouter.GetProvider(effectiveSettings)` → `IAiProvider.GenerateAsync`,
   always built from `PreseMakerCredentials.CreateEffectiveSettings(store.Load())`, keys DPAPI
   at rest (`AiSettingsStore`), every call through `ActivityLog`. Do not extend the copied
   layer; put new AI capability in `..\AiManager`.
4. **The tree is the navigation, the editor is per node.** `MainWindow` builds `TreeViewItem`s
   whose `Tag` is the model object (Book, Section, Chapter, Item, Character, TimelineEvent,
   Plotline); selection change commits the previous node and shows the next. Context menus per
   node type add children (+ Section, + Chapter, + Item ▸ kind), rename, move, delete — the
   CIATLE `NavigationTree` pattern. Simple records edit through `FieldForm(...)` — label +
   TextBox per property, writing straight into the model — so adding a field to a model is one
   line in `ShowCurrent`. Item kinds each get a small editor (Summary/Notes: text; Analysis:
   read-only body with a metadata header and Re-run; Characters in chapter: checklist + POV).

## Conventions

- Models are plain classes with public setters (System.Text.Json); enums serialise as strings.
- Ids are `Guid`s; cross-references (`ChapterIds`, `CharacterIds`, `PlotlineIds`) are id lists,
  resolved at display time. Never store display names as references.
- UI text is plain English for authors, not developers ("chapter", "book folder"), and every
  destructive action confirms first.
- No third-party UI packages yet; if one is needed it must be MIT/BSD/Apache (Store app).
- Windows-only APIs (DPAPI, WinForms folder dialog) are guarded or confined to the App project
  so Core/AI tests run anywhere.

## Sibling apps worth knowing

- `..\CIATLE\CIATLE.AICore` — the origin of `src/AuthorPlus.AI` (settings form patterns there
  are WinForms; do not copy them).
- `..\PreseMaker` — owns the shared DPAPI key file this app can borrow, and the Google TTS
  narration pipeline the "narrations" feature will reuse.
- `..\SMADA10` — the WPF/.NET 10 + scripted-MSIX Store pattern to follow for packaging
  (`packaging/pack.ps1`, `AppxManifest.xml`).
