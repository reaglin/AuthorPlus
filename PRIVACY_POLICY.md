# Privacy policy — Author+

**Last updated: 15 September 2026**

Author+ ("the app") is a Windows application for writing and organising a book, published by
Ron Eaglin. This policy explains what the app does with your writing and your settings. It is
short because the app collects almost nothing.

## The short version

Your book stays on your computer. The developer receives no data from the app — no accounts, no
telemetry, no crash reports, no analytics.

The app **provides** two things that can send something out, and **requires** neither of them:
AI features, which use an API key of your own, and Git version control for a book, which can
optionally be copied to a GitHub repository of your own. Both are features you switch on, book by
book and request by request. Used or not, nothing goes to the developer, and nothing goes anywhere
at all until you ask for it.

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
| The version history of a book, if you turn versions on for it | inside that book's own folder, in `.git\` | An ordinary Git repository, on your computer. Delete the `.git` folder and the book is exactly as it was, minus its history. |
| Your GitHub access token, if you choose to back a book up there | `Documents\AuthorPlus\github.json` | Encrypted with Windows DPAPI, tied to your Windows user account. Remove it from inside the app with *Sign out*, or delete the file. |

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

## Version history, and the optional GitHub backup

Author+ **provides Git version control for a book**: it can remember the book as it stood at any
moment, show what changed between two versions, and put a chapter — or the whole book — back as it
was. This is an ordinary Git repository inside your own book folder, and **it is entirely local**.
Keeping versions sends nothing anywhere.

Separately, and only if you ask for it, that history can be copied to a **GitHub** repository of
your own, so that losing your computer does not lose your book, and so that a book written on two
machines stays in step.

- **Neither is required.** A book keeps no versions until you turn them on, and a book with
  versions goes nowhere until you sign in to GitHub and name a repository. Author+ is a complete
  writing tool with both left alone.
- **It is your GitHub account and your repository**, not the developer's. You supply a personal
  access token; it stays on your computer, encrypted for your Windows user, and is sent to nobody
  but GitHub. The developer has no access to your account, your token or your repository, and
  operates no server in between.
- **What is sent** is the book folder as you would see it in Explorer: the manuscript, characters,
  timeline, plotlines, notes and any AI results you kept, together with the versions you saved.
  That is what a backup is; there is nothing extra hidden in it.
- **Private by default.** The app offers to create the repository as private, and says why. If you
  choose to make it public, or connect the book to a repository that is already public, anyone can
  read your manuscript. That is your decision, and the app will not make it for you.
- **What GitHub does with what you send is GitHub's business**, under their terms and privacy
  policy, not this app's.
- **You can stop at any time.** *Sign out* removes the token from this computer. Deleting the
  repository on GitHub removes the copy there. Deleting the `.git` folder inside a book ends its
  version history and leaves the book itself untouched.

## What the app does not do

- It has no user account of its own, and never asks you to sign in to anything belonging to the
  developer. The only sign-in it offers is to your own GitHub account, if you want the backup.
- It sends no usage statistics, telemetry, crash reports or analytics to the developer.
- It contains no advertising and no third-party trackers.
- It does not read files outside its own folders unless you explicitly import a document.
- It never uploads your book on its own account, and it never uploads it to the developer. The
  only copy that can leave your computer is the GitHub backup described above — a repository you
  set up yourself, on your own account, when you choose to.

## Children

Author+ is a writing tool for general audiences. It is not directed at children and collects no
information from anyone, of any age.

## Changes to this policy

If the app starts doing something this policy does not describe, the policy will be updated before
that version ships, and the date at the top will change.

## Contact

Ron Eaglin — ron.eaglin@gmail.com

Published at https://punchmonkeyserver.com/privacy/authorplus
