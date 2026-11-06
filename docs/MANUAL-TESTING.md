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

## Round 2026-11-06 — the editor's modes (plan phase 6)

Build: `manual-test\AuthorPlus.exe` (made 2026-11-06). Close any copy that is open first. Open *The Book of One* and a
chapter. Record each step as ✅ / ❌ with a note; ❌ and notes go back into `docs/DEVELOPMENT-PLAN.md`, phase 6.

### M. The modes (6.1)
M1. Above the chapter: **✎ Write · ¶ Analysis · 👤 Characters**, with a line saying what the mode is for. Write is ticked;
    its toolbar has the editing tools only — no Summarize / Analyze / Chapter Characters / Suggestions buttons (they are in
    Analysis mode and the AI menu).
M2. **Ctrl+2**, **Ctrl+3**, **Ctrl+1** (or View › … Mode) switch modes; the chapter stays at the same place in the text.
    Click another chapter in the tree — it opens in the same mode.
M3. In Analysis and Characters the text cannot be typed into: type a letter — a yellow note says so. **Double-click** a word —
    it goes back to Write with the caret there.

### N. Analysis mode (6.2, 6.3)
N1. Every paragraph has a grey **¶** in the left margin (scene breaks and one- or two-word lines do not). Hover: it says
    "not read yet". The panel on the right shows **This chapter**: Summary, Characters, Plotlines, Chapter analysis, Paragraphs.
N2. On a chapter with no summary: **Write the summary (AI)** — it is written, the card shows it, and you stay on the chapter.
    Do the same with **Find who is in it (AI)**, **Find its plotlines (AI)** and **Analyze the chapter (AI)…** / **One aspect ▾**.
    **Open** on a card opens that item in the tree.
N3. Click a paragraph (or its **¶**) — the panel shows that paragraph, a band marks it in the text. Type what it should do (optional),
    **Read this paragraph (AI)…**, Send Prompt, then **Next: see it beside the paragraph →**: the read and its rewrites are in
    the panel; the ¶ is amber.
N4. **Apply** a rewrite — the text changes in place, the box above says "The passage before your change"; when every rewrite
    is applied, marked or resolved the ¶ turns green ("dealt with"). **Undo apply** puts the original back. **Dealt with**
    turns it green by hand. **‹ Previous / Next ›** walk the paragraphs.
N5. Switch to Write, change a few words of a read paragraph, back to Analysis — the read is still on it, marked "the paragraph
    has changed since this read". **Delete this read** removes it (the text stays).
N6. Close and reopen the book: the reads and colours are still there. The tree does not list the reads.

### P. Character mode (6.4)
P1. Each character's name — and their "also called" — is washed in their own colour; names that are not characters yet are
    underlined with dots. The panel lists the chapter's cast with how often each is named.
P2. Click a coloured name — their form opens beside the text; **◀ Previous / Next ▶** select each mention in turn. Edit a field
    (Edit…): it is saved like on their own page. Change their name at the top: the tree follows.
P3. A character named in the text but not on the chapter's list shows "not on the chapter's list" — the button adds them.
P4. Click a dotted name: **Add as a character** adds them and opens their form; or pick a character and **Add as their other
    name** — the name is then coloured as them. For a place: **Not a character — stop marking it**; it moves to "Not marked"
    in the panel, where "mark again" undoes it.
P5. An "also called" written with a capital (Father) colours "Father" but not "her father".

### Q. Look (the review could not judge these from the code)
Q1. The white ¶ on the amber and green icons — readable? The colour washes behind the text (yellow, teal especially)?
Q2. At 150 % scaling: the panel (430 px) — nothing cut off; the mode bar's line of help wraps tidily.

## Round 2026-11 — the 1.0.0 release (Export / Import Project)

Build: `manual-test\AuthorPlus.exe` (made 2026-11-05, version 1.0.0, AI Manager 1.7.0). Close any copy that is open first.
Record each step as ✅ / ❌ with a note; ❌ and notes go back into `docs/DEVELOPMENT-PLAN.md`.

### A. Export / Import Project (plan 4.12) — the new feature
A1. Open *The Book of One*. **File › Export Project (to work on another computer)…** — expected: it saves first, offers
    `The Book of One 2026-11-05 HHmm (<this computer>).authorplus` in Documents, and after a moment says how many files and
    MB (about 1 MB), what to do on the other computer, and offers to show the file. The window is locked while it works.
A2. **File › Import Project…** and choose that same file — expected: it says the file was made on this computer and that
    it holds exactly what is here (nothing new); Replace is ticked. Press **Cancel**: nothing changes.
A3. On the other computer (or: copy the file somewhere else, delete nothing): **Import Project** there — expected: "not on
    this computer yet", Import opens the book with its chapters and Version History.
A4. Write a sentence there, Export Project there, bring the file back and **Import Project** here — expected: "The file
    has new writing… Replacing it is the usual choice", Replace ticked. Import — the sentence is there; the old copy is in
    `Documents\AuthorPlus\Replaced books\The Book of One (replaced …)` and the message says so.
A5. Write here *and* on the other computer before importing — expected: ⚠ "Both computers have new writing…", **Keep both**
    ticked; Import adds "The Book of One (2)". Then Import again: it asks which of the two copies the file is for.
A6. Choose a `.docx` with "All files" — expected: "This is not an Author+ project file…", pointing to Insert Chapter from File.

### B. Same as every program (USABILITY-CHECKLIST 5–6)
B1. The window title, About and messages say **Author+** (the Store name).
B2. **Help › About Author+**: version 1.0.0; four links — softwareplus.ai/author, the Microsoft Store page
    (apps.microsoft.com/detail/9MZCBQVSSDVG — works once the Store listing is live), all programs.
B3. **Help › Check for a new version…** — this hand-test copy says it cannot check and offers the Store page.
B4. **AI › AI Settings…** — AI Manager 1.7.0: How? beside Get a key for your provider (plan R.1/R.2).

### C. Look
At 150 % scaling: the Import Project window (its Replace line holds a full folder path) — nothing cut off; the ⚠ box readable.

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
