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

**Story bible**
- Chapter: the "Characters present" (tick + POV) and "Plotlines" checklists above the text.
- Character page: "Appears in", **Scan chapters for mentions…** (aliases count — one per line
  under "Also called"). Add a plotline or two, then the **Plotlines** board (click cells) and
  the **Timeline** view (add events, link chapters, move them; watch the ⚠ column).
- Book › **Check Consistency…** (Ctrl+K); tick "Also scan chapter text"; double-click a line.

**AI** (keys come from the AI Manager; AI › AI Settings shows them and this month's spend)
- Right-click a chapter › **Summarize** (summary item appears under the chapter);
  **Analyze…** on one provider, then on "Every provider with a key" to compare;
  **Characters in This Chapter**; **Style Report…** then **AI voice read…**.
- Right-click a section › **Outline This Section**, **Summarize This Section**,
  **Continuity Check (AI)…**, **Plot Analysis (AI)…** (these want chapter summaries first).
- Character › **Suggest Profile (AI)**.
- View › **Preview AI Prompts Before Sending** — turn it on and run anything: you see the
  exact text, tokens and cost before it goes. AI › **Prompt Library…** to change the wording;
  AI › **AI Usage…** for the month's calls.

## Known gaps (not bugs)

- No EPUB / PDF export yet (phase 4); no MSIX / Store packaging yet (phase 4).
- Multi-select delete in the tree is not implemented (WPF TreeView is single-select).
- Part 3 chapter 23 is not on disk — insert it when found (task 1.19).
- Several Part 2 chapter titles differ between file name and in-document heading; titles came
  from the file names. Rename in the tree, or re-import Part 2 with "Titles from: headings".
- The OpenAI key has no credits on that account; Claude and Gemini work.
- Preview-prompts and the tree's expanded state are not remembered between runs.
- Audio / text-to-speech is version 2 (see "Version 2 — Audio" in the plan).
