# Privacy policy — Author+

**Last updated: 15 September 2026**

Author+ ("the app") is a Windows application for writing and organising a book, published by
Ron Eaglin. This policy explains what the app does with your writing and your settings. It is
short because the app collects almost nothing.

## The short version

Your book stays on your computer. The developer receives no data from the app — no accounts, no
telemetry, no crash reports, no analytics. Anything leaves your computer only when **you** ask for
it, and then it goes straight from your computer to a service **you** chose and signed in to: an
AI provider, when you run an AI feature with your own API key, or GitHub, when you back a book up
to a repository of your own.

## What the app stores, and where

Everything the app stores is on your own computer, in your user profile:

| What | Where | Notes |
|---|---|---|
| Your books — chapters, characters, timeline, plotlines, notes, AI results | `Documents\AuthorPlus\Books\<book title>\` | Plain files (JSON and XAML). You can read, copy, back up or delete them yourself at any time. |
| Your AI provider API keys | `Documents\AiManager\` | Encrypted with Windows DPAPI, tied to your Windows user account. Shared with other Eaglin apps that use the same AI layer, so you only enter a key once. |
| A record of AI requests — date, provider, model, purpose, token counts, estimated cost | `Documents\AiManager\` | So the app can show you what you have used. The text of your prompts is not kept in this ledger. |
| An activity and error log | `Documents\AiManager\logs\` | For diagnosing problems on your own machine. |
| Your edits to the AI prompt wording | `Documents\AiManager\` | |
| The list of books you opened recently | `%APPDATA%\AuthorPlus\` | So the app reopens your last book. |
| Your GitHub access token, if you back a book up to GitHub | `Documents\AuthorPlus\github.json` | Encrypted with Windows DPAPI, tied to your Windows user account. Delete it from inside the app with *Sign out*, or delete the file. |
| The history of a book you keep versions of | inside that book's own folder, in `.git\` | An ordinary git repository. Delete the `.git` folder and the book is exactly as it was, without its history. |

Uninstalling the app does not delete your books. They are your files; delete the folders yourself
if you want them gone.

## The AI features

Author+ can send parts of your manuscript to an AI provider to summarize a chapter, analyse it,
suggest rewrites of a passage, draft a character profile, or extend a plotline summary. This only
happens when you press one of those buttons.

- **You choose the provider.** Anthropic (Claude), Google (Gemini), OpenAI, Mistral or xAI.
- **You supply the API key.** The request goes from your computer straight to that provider. It
  does not pass through any server belonging to the developer.
- **What is sent** is what the request needs: the chapter or passage you are working on, and
  context you have written such as summaries, character notes or your instructions to the AI. The
  app can show you the exact text of any request before it is sent (turn on
  *View › Preview AI Prompts Before Sending*).
- **What happens to it then is the provider's business, under their terms and privacy policy, not
  this app's.** Read the policy of whichever provider you use. Providers differ on whether they
  retain prompts and for how long.
- If you never enter an API key, the app never contacts an AI provider, and nothing at all leaves
  your computer.

## The GitHub backup

A book can be kept as a series of versions, and that history can be copied to a **GitHub**
repository so that losing your computer does not lose your book. This only happens if you set it
up, book by book.

- **You choose whether to use it at all.** A book keeps no versions until you say so, and it goes
  to GitHub only after you sign in and name a repository.
- **It is your GitHub account and your repository.** You supply a personal access token, which
  stays on your computer, encrypted for your Windows user. The developer has no access to your
  account, your token or your repository.
- **What is sent** is the book folder: the manuscript, characters, timeline, plotlines, notes and
  any AI results you kept — and every version of them you have saved. That is the point of a
  backup; there is nothing extra hidden in it.
- **Private by default.** The app offers to create the repository as private, and says why. If you
  choose to make it public, or connect the book to a repository that is already public, anyone can
  read your manuscript. That is your decision to make, not the app's.
- **What GitHub does with it is GitHub's business**, under their terms and privacy policy, not
  this app's.
- If you never connect a book to GitHub, nothing about your book ever leaves your computer through
  this feature.

## What the app does not do

- It has no user account of its own, and never asks you to sign in to anything belonging to the
  developer. The only sign-in it offers is to your own GitHub account, if you want the backup.
- It sends no usage statistics, telemetry, crash reports or analytics to the developer.
- It contains no advertising and no third-party trackers.
- It does not read files outside its own folders unless you explicitly import a document.
- It does not upload your book anywhere on its own. The only copy that ever leaves the computer
  is the GitHub backup described above, to a repository you set up yourself.

## Children

Author+ is a writing tool for general audiences. It is not directed at children and collects no
information from anyone, of any age.

## Changes to this policy

If the app starts doing something this policy does not describe, the policy will be updated before
that version ships, and the date at the top will change.

## Contact

Ron Eaglin — ron.eaglin@gmail.com

Published at https://punchmonkeyserver.com/privacy/authorplus
