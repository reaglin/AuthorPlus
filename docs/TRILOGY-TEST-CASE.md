# The real book — Ron's trilogy as the live test case

Ron is using AuthorPlus in real time on a book he has written (2026-09-12). Every feature is
designed and hand-tested against it. This file records what is on disk, what the first import
should produce, and what to check.

## Where the manuscript is

`C:\Users\ronal\OneDrive - Daytona State College\Documents\My Books\`

One AuthorPlus book, **The Book of One**, with three sections (decision 2026-09-12):

| Section | Folder | Chapter files | Notes |
|---|---|---|---|
| Part 1 — The Power of One | `The Power of One\Final Draft - Part 1\` | 28 (`Chapter One - The Message.docx` … `Chapter Twenty-Eight - The Final Article.docx`) | numbers spelled out. Same folder holds `The Book of One - Part One.docx` (compiled) and three AI evaluations: `Evaluation - ChaptGPT (2).docx`, `Evaluation - Gemini (1).docx`, `Evaluation - Grok(3).docx` |
| Part 2 — The Corruption of Two | `The Corruption of Two\` (top level) | 19 (`Chapter 1 - The intelligence.docx` … `Chapter 19 - Eplilogue.docx`) | numeric. `Draft 1\` is an older 21-chapter draft — **do not import**. `..\The Book of One Part 2 Final.docx` (2025-10-29) is the compiled final; confirm the top-level chapter files match it |
| Part 3 — The Soul of Three | `The Soul of Three\` | 26 (`Chapter 1 - The Revealing of AGI.docx` … `Chapter 27 - Finale.docx`) | **no chapter 23** on disk — ask Ron whether it was cut or lives elsewhere. `The Soul of Three - Draft One.docx` is the compiled draft |

Also in the Part 1 folder: `Power of One Prompts.txt` and `Chapter Summary - Power of 2.txt`
(Ron's own AI prompts and chapter-flow outline for Part 2). These show how Ron already works:
he pastes the previous novel for continuity and writes numbered chapter-flow outlines. Both
are feature targets (continuity check against earlier sections; a per-section outline item).

## What the chapter files look like

Inspected `Chapter One - The Message.docx` (2026-09-12): 43 paragraphs, one `Heading1` (the
chapter title, e.g. "Chapter One - The Message"), the rest `Normal`; 2 bold runs, no italics,
about 2,350 words. Styles present: Normal, Heading1–6, Title, Subtitle. So the importer needs:
paragraphs, heading styles, bold/italic runs. Nothing else (no tables, images, footnotes).

## Expected result of the first import

- Book **The Book of One**, author Ron Eaglin, three sections in order.
- Chapter titles taken from the file name after the dash (`The Message`), the number from the
  prefix (spelled-out or numeric), order by number. Where the DOCX's Heading1 differs from the
  file name, keep the file name and note the difference in the import summary.
- Word counts populated; status `Draft` for all.
- Total: 73 chapters. Rough word count to confirm on import.
- The originals are never modified. The book folder is
  `Documents\AuthorPlus\Books\The Book of One\`.

## Hand-test script (after each phase)

1. Open the book; expand Part 1; click Chapter One; the prose shows with the heading.
2. Add an item under Chapter One: Summary (AI) — check it lands under the chapter node and
   in `items\`.
3. Run Analysis on Chapter One with two providers; both appear as dated items.
4. Add character "Roland" and "Lucy Dalgo"; "Characters in this chapter" on Chapter Twelve
   should propose both.
5. Close and reopen; everything is where it was.

## Notes from Ron's use

- **2026-09-12 (Claude, first load).** Imported through Core into
  `Documents\AuthorPlus\Books\The Book of One`: Part 1 28 chapters / 68,110 words, Part 2 19 /
  57,791, Part 3 26 / 36,977 — 73 chapters, 162,878 words, 146 files in `chapters\`. The scan
  flagged: Part 3 missing chapter 23; title mismatches inside the files — Part 1 ch. 3 ("…?
  by Roland Ellison" in the heading) and 25 ("What Next?"); Part 2 ch. 5 (heading "Interlude -
  George"), 8 ("The Interrogation" vs file "Roland"), 10 ("The Fallout" vs "The Safehouse"),
  11 ("The Exchange" vs "The Extraction"), 12 ("Edmund's Victory" vs "Edmund"), 18 ("The Heart
  and Mind"). File-name titles were used; Ron may prefer the in-document ones for Part 2 —
  the Import window has a "Titles from" switch, or rename in the tree. Skipped correctly: the
  compiled drafts, the three AI evaluation docs, the cover image. Hand-test steps 1, 2 and 5
  pass (open, Summarize → item, save/reopen); steps 3 and 4 await Ron.
