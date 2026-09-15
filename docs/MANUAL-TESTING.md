# Manual testing — AuthorPlus

Where the build is, what is in it, what to exercise, and what is knowingly missing.
Written 2026-09-13 after phases 0–3 (`DEVELOPMENT-PLAN.md`). Feedback goes under
**Notes from hand-testing** in that file (or in `TRILOGY-TEST-CASE.md` for anything about
the trilogy itself); each note becomes a task.

## The build

```
C:\Users\ronal\source\repos\AuthorPlus\manual-test\AuthorPlus.exe
```

Rebuild with `.\build-manual-test.ps1` from the repo root (framework-dependent single file;
uses the .NET 10 runtime on this machine). `-SelfContained` makes a build for a machine without
.NET 10. `manual-test\` is gitignored. The exe carries the AI Manager DLLs inside it; the keys,
prompts, log and usage ledger are the shared ones in `Documents\AiManager\`.

Open the trilogy with **File › Open Recent › The Book of One**, or drag the book folder onto
the exe (`AuthorPlus.exe "<book folder>"` opens it). Books live in `Documents\AuthorPlus\Books`.

## What to exercise

**Tree and editing**
- Book → Part 1/2/3 → numbered chapters → items under them; Characters, Timeline, Plotlines.
  Right-click any node for its menu. Drag a chapter onto another chapter (moves before it, into
  that section), onto a section (to its end) or onto the book (out of any section). Alt+Up/Down.
- Chapter editor: undo/redo, B/I/U, alignment, lists, font size, **Heading**, **Scene break**,
  **Find…** (Ctrl+F: next / replace / replace all), **Goal** (per-chapter words), Status.
- Autosave 12 s after you stop typing and when the window loses focus — watch the status bar
  ("Unsaved changes" → "Autosaved hh:mm:ss"). Ctrl+S any time.
- F11 distraction-free writing; Esc or F11 back.
- Status bar: chapter / book word counts against goals, words added today.
- Book › Import Chapters from Folder… (try the Part 3 folder: it reports the missing 23) and
  right-click a chapter › Insert Chapter from File After This… (a DOCX, TXT or MD).
- File › Export › Word document / Markdown — open the result in Word.
- File › Export › **PDF** — the setup window offers 6 × 9 in (a paperback page), Letter or A4,
  a straight or ragged right margin, and page numbers. The whole trilogy takes about a second
  and comes out around 647 pages. Worth looking at in a reader: chapter openings and their drop,
  the first-line indents, the spacing of justified lines, that no single line is stranded at the
  foot or top of a page, the page numbers, and the bookmarks panel (chapters nested under their
  part). Select a paragraph in the reader and copy it — the words should have their spaces.
- File › Export › **EPUB e-book** — open it in your e-reader or Calibre: a title page, a page
  per part, one chapter per file, and a table of contents that nests the chapters under the part.

**Versions and GitHub** (new, 2026-09-15 — the full script is in `VERSIONS-AND-GITHUB.md`)
- Versions › **Start Keeping Versions…** on the trilogy (about two seconds), then write a
  paragraph and **Save a Version…** (Ctrl+Shift+S): the name offered should already say which
  chapter changed, and the list under it should read in chapter and character names.
- **Version History…** (Ctrl+H): what each version changed; double-click a chapter line for the
  compare view (prose, with the moved words marked); **Put the book back to this version…** —
  the tree reloads and the history grows rather than shrinks.
- **This Chapter's History…** on a rewritten chapter, then *Put the chapter back to this version*.
- **Back Up to GitHub…**: the window carries the five steps for making a token and a link to the
  GitHub page that arrives with the right permission ticked. Sign in, keep the suggested name,
  leave Private ticked, *Create it and back up the book*. Then *Back up now* and *Get changes from
  GitHub*. Done for real 2026-09-15 — *The Book of One* is on the private
  `github.com/reaglin/The_book_of_one`. The menu item is live even before a book keeps versions:
  it offers to start them as part of setting the backup up.
- Close the book with unsaved work: a version is saved by itself (untick *Save a version when I
  close the book* to stop that). The status bar shows `12 versions · 3 not saved`; click it.

**Story bible**
- Chapter: the "Characters present" (tick + POV) and "Plotlines" checklists above the text.
- Character page: "Appears in", **Scan chapters for mentions…** (aliases count — one per line
  under "Also called"). Add a plotline or two, then the **Plotlines** board (click cells) and
  the **Timeline** view (add events, link chapters, move them; watch the ⚠ column).
- Book › **Check Consistency…** (Ctrl+K); tick "Also scan chapter text"; double-click a line.

**AI** (keys come from the AI Manager; AI › AI Settings shows them and this month's spend)
- Right-click a chapter › **Summarize** (a Summary item: "What happens" bullets, then
  "Characters" with who is introduced);
  **Analyze…** on one provider, then on "Every provider with a key" to compare — the run window
  says what it will produce; the Analysis item opens as one card per aspect (Pacing, Tension,
  Stakes, Point of view and voice, Dialogue, Continuity, Prose habits, Three changes) with the
  editor's numbered points, each point with its own **Suggestions…** (rewrites for that one
  point, saved under the analysis as "Suggestions · Aspect · n"). Open a Suggestions item: a
  diff per suggestion (current passage left with removed words struck through, rewrite right
  with added words highlighted), the reasoning, and **Apply** (replaces the passage in the
  chapter), **Mark** (keeps it for your own rewrite — a box appears, then **Apply mine**),
  **Resolve**, **Go to passage**. **Marked Passages…** (right-click a chapter, or Book menu for
  the whole book) brings every marked passage back. Right-click any item › **Mark as
  Resolved** puts a ✓ on it in the tree. **Ask again…** on a suggestion opens a form —
  the passage, the suggestion, its reasoning, moods to pick from, and a box for what you are
  trying to convey; the new rewrites land under the one you asked about, labelled with what you
  asked for. Every run window now ends with a **Next: …** button saying what to do next. When the run closes, a
  short pass lists the chapter's characters and plotlines: known ones are linked, new ones are
  offered in a checklist, and **Chapter Characters** / **Chapter Plotlines** items appear;
  **Chapter Characters** on its own; **Style Report…** then **AI voice read…**.
- AI › **Analyze One Aspect** (or right-click a chapter › Analyze one aspect): a deeper read of
  Pacing, Tension, Stakes, Point of view and voice, Dialogue, Continuity or Prose habits alone,
  each with its own prompt. **Go deeper…** on an aspect card of an existing analysis does the
  same. What you write in a chapter's Notes is passed on as what you are aiming for.
- Right-click a section › **Outline This Section**, **Summarize This Section**,
  **Continuity Check (AI)…**, **Plot Analysis (AI)…**, **Find Plotlines (AI)…** (these want
  chapter summaries first). Find Plotlines proposes the threads — primary / secondary /
  subplot — with the chapters each runs through; tick the ones to keep and they are added and
  linked. A plotline's kind can be changed on its page; the board shows it.
- **Suggestions for a passage you chose.** Two ways in, both leading to the same form where you
  say what the passage is for (the mood chips and your own words) before anything is sent:
  select text in the chapter editor and use the toolbar's **Suggestions… (AI)** or
  AI › **Suggestions for Selection…**; or use AI › **Choose a Passage…** (also on a chapter's
  right-click menu) to see the chapter paragraph by paragraph with a **Select** on each — tick
  several to take them together. The rewrites are saved as a Suggestions item on the chapter.
- **The chapter editor is now only the manuscript.** Who is in the chapter and which threads run
  through it show in the **status bar** ("Characters: 4 · POV Roland ▸", "Plotlines: 3 ▸"); each is a
  link to that page, and hovering shows the full list.
- **Characters page**: the cast table as before, and under it a chart of one row per character and
  one column per chapter. ● means they are in the chapter, ★ that it is told from their point of
  view. Click a cell to put them in or take them out; double-click to make them the point of view.
  This is where those links are made now.
- **Chapter Characters** and **Chapter Plotlines** items are tables to read, not forms. Plotlines
  shows the name, the type as a tinted chip, whether the thread is introduced here or continued
  from an earlier chapter, and its description; every name opens the plotline, where it is edited.
  Characters shows the name, role, point of view or where else they appear, and their other names.
  The AI’s notes stay editable under each table.
- **A character page** is now bullets with an **Edit** button per field: Role, Also called, Origin,
  Physical description, Personality, Motivations, Actions, Arc, Notes. Edit opens the whole field
  in one box, one entry per line. **Fill in from the book (AI)** reads the chapter summaries and
  adds what it finds as its own entries, each marked **AI** — change, unmark or delete any of them.
- **A plotline page** is the same pattern over About where it stands, Summary and Notes, and its
  status is now derived from its chapters: planned until it runs anywhere, active once it does,
  resolved when a chapter resolves it. **Add to the summary from the chapters (AI)** reads the
  summaries of the chapters it runs through and adds what they say.
- **The plotlines chart** now colour-codes what a thread does in each chapter: green introduced,
  blue continuing, purple resolved. Click a cell for the menu that sets it.
- **The characters chart** reads the same way: green where a character is introduced, blue where
  they appear, purple where they leave the story, and a star for the chapter told from their point
  of view (the star keeps the colour of their part). Clicking a cell opens the same kind of menu,
  with point of view as a fourth choice in it.
- View › **Preview AI Prompts Before Sending** — turn it on and run anything: you see the
  exact text, tokens and cost before it goes. AI › **Prompt Library…** to change the wording;
  AI › **AI Usage…** for the month's calls.

## Known gaps (not bugs)

- The export runs on the window's own thread: the cursor turns to the hourglass and the status
  bar says what it is doing, but the window will not repaint while it works (about a second for
  the trilogy). If that ever feels stuck, it moves to a background thread.
- Multi-select delete in the tree is not implemented (WPF TreeView is single-select).
- Plotline kinds now include **chapter** (opens and closes in one chapter) and **extra**; existing
  plotlines keep the kind they had.
- Part 3 chapter 23 is not on disk — insert it when found (task 1.19).
- Several Part 2 chapter titles differ between file name and in-document heading; titles came
  from the file names. Rename in the tree, or re-import Part 2 with "Titles from: headings".
- The OpenAI key has no credits on that account; Claude and Gemini work.
- Preview-prompts and the tree's expanded state are not remembered between runs (the last book is reopened on start).
- Audio / text-to-speech is version 2 (see "Version 2 — Audio" in the plan).
- Versioning has no branches, merges or staging, and a book cannot yet be cloned *from* GitHub
  inside the app (copy the folder, or `git clone` it, then File › Open Book).
