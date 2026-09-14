# Privacy policy — Author+

**Last updated: 14 September 2026**

Author+ ("the app") is a Windows application for writing and organising a book, published by
Ron Eaglin. This policy explains what the app does with your writing and your settings. It is
short because the app collects almost nothing.

## The short version

Your book stays on your computer. The developer receives no data from the app — no accounts, no
telemetry, no crash reports, no analytics. The only time anything leaves your computer is when
**you** run an AI feature, and then it goes directly from your computer to the AI provider you
chose, using your own API key.

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

## What the app does not do

- It has no user accounts and never asks you to sign in.
- It sends no usage statistics, telemetry, crash reports or analytics to the developer.
- It contains no advertising and no third-party trackers.
- It does not read files outside its own folders unless you explicitly import a document.
- It does not upload your book anywhere for storage or backup.

## Children

Author+ is a writing tool for general audiences. It is not directed at children and collects no
information from anyone, of any age.

## Changes to this policy

If the app starts doing something this policy does not describe, the policy will be updated before
that version ships, and the date at the top will change.

## Contact

Ron Eaglin — ron.eaglin@gmail.com

Published at https://punchmonkeyserver.com/privacy/authorplus
