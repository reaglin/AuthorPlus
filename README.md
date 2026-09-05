# AuthorPlus

An AI-enhanced authoring tool for writing a book and keeping it straight.

A book is more than its chapters. AuthorPlus organises the manuscript and everything around it
in one tree:

- **Chapters** — written in a rich-text editor, reordered by drag or keyboard, each with a
  status (outline → draft → revised → final), a summary and a word count.
- **Characters** — name, role and origin, and then what actually matters: motivations,
  actions across the story, and how they change.
- **Timeline** — the story in linear order even when the chapters are not, so dates, ages and
  sequences stay consistent.
- **Plotlines** — the threads of the book, where each converges with the others.

AI assists rather than writes: draft a chapter summary, suggest a character profile from what the
chapters already say, and (planned) check continuity and analyse style. You bring your own API key
for Claude, Gemini, OpenAI or Mistral; keys are stored encrypted for your Windows account.

Your book is a folder of small files — one per chapter, character, event and plotline — under
`Documents\AuthorPlus\Books`. Nothing is locked in a database; back it up, put it in git, or read
any chapter with a text editor.

**Platform:** Windows 10/11 · WPF on .NET 10 · Microsoft Store.
**Status:** early development — see `docs/DEVELOPMENT-PLAN.md`.

## Building

```powershell
dotnet build AuthorPlus.sln
dotnet test tests/AuthorPlus.Tests
dotnet run --project src/AuthorPlus.App
```

Requires the .NET 10 SDK.

© Ron Eaglin
