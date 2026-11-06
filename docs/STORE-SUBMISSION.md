# Author+ — Microsoft Store submission

Everything needed to put **Author+** in the Microsoft Store, in the order to do it, with the exact
values to type and the exact commands to run. Tick each box as you go. Nothing here is guesswork
except the two items marked **NEEDS YOU**, which only exist in Partner Center.

Publisher account: sign-in `ron.eaglin@gmail.com`, publishing as **Dean Eaglin**
(https://apps.microsoft.com/search/publisher?name=Dean+Eaglin). Same account as SMADA and
Statistle, so most of the identity is already known.

## Version 1.1.0 — voices, the audiobook and the editor's modes (Ron, 2026-11-06)

1.0.0 went live on 2026-11-06. 1.1.0 is an update of the same product (identity `DeanEaglin.Author`, Store ID 9MZCBQVSSDVG):
**Partner Center › Author+ › Update** (a new submission copies the last one; only what changed below needs touching).

| | Item | State |
|---|---|---|
| ☑ | AI Manager **1.9.0** (the latest; speech) | pinned 2026-11-06 |
| ☑ | Version **1.1.0** in `Directory.Build.props` | 2026-11-06 |
| ☑ | Build clean (Release), **198 tests** green | 2026-11-06 |
| ☑ | **`artifacts\AuthorPlus_1.1.0.0_x64_bundle.msixupload`** (80.4 MB), real identity, family name `DeanEaglin.Author_xs303fgqvwdg8` checked; NAudio and AI Manager inside | 2026-11-06 — **upload this first** (Packages) |
| ☑ | WACK on the 1.1.0 bundle — **PASS**, 23 of 24 (the one failure, Blocked executables, is optional — as in 1.0.0) | 2026-11-06 |
| ☑ | Privacy policy updated for the voices (Google Cloud Text-to-Speech) and deployed | https://softwareplus.ai/author/privacy/ — 2026-11-06 |
| ☐ | Ron's hand-test of `manual-test\AuthorPlus.exe` (rounds 2026-11-06: M–W) | before submitting |
| ☑ | Package uploaded by Ron; validated | 2026-11-06 |
| ☑ | Store listing: What's new, the description's two new parts, features 17–20, ten screenshots (the six new first, then 1.0's write-a-chapter, AI analysis, characters chart, plotlines chart) | 2026-11-06 (search terms left as they were) |
| ☑ | Notes for certification: the voices paragraph (Additional Testing Information) | 2026-11-06 |
| ☑ | **Submitted for certification** — "Update in certification", publishes as soon as it passes | 2026-11-06 |
| ☐ | After it is live: SoftwarePlus.ai page, the examples (plan 10.12), tag `v1.1.0` | §12 |

**Packages:** upload `AuthorPlus_1.1.0.0_x64_bundle.msixupload`; remove the 1.0.0 package from the submission if Partner Center
keeps it (the Store would serve the higher version anyway).

**What's new in this version** [1500 — this text is 865]

```
Voices and audiobooks. Hear any chapter read aloud: choose a narrator and a voice for each character (Google's voices, with a Google key of your own), let the AI mark who says each line, and listen. Approve a chapter when it sounds right, then export the book as an audiobook: an MP3 per chapter at the stores' standard and one M4B with chapter marks. A pronunciation list makes the voices say your names your way.

Six editor modes. Write; Create, where the AI writes at the place you choose, in your voice and the style you describe, or drafts a new chapter for you to read first; Analysis, for a paragraph's tone and style or suggested changes, and the chapter's summary and analysis; Characters, with every name coloured and each character as this chapter shows them and across the book; Plotlines, with each thread's key moments marked in the text; and Listen.
```

**Description — add after "AN EDITOR BESIDE YOU"** (the rest stays):

```
SIX WAYS TO LOOK AT A CHAPTER
· Write — the editor, and nothing else
· Create — click a + between two paragraphs, say what should happen and how it should be written, and the AI writes it there in your voice; you read it before it goes in. Or have it draft a whole new chapter. The styles you describe are kept, to choose again
· Analysis — a coloured mark beside every paragraph: an analysis of its tone, consistency, style and what it achieves, or suggested changes you can apply and undo
· Characters — every character's name coloured where the chapter uses it; each one as this chapter shows them (their role here, how they are described, what they want and do) and across the book (their arc, the plotlines they touch)
· Plotlines — the threads in the chapter, their key moments marked in the text, and each thread across the book; a character's own story can be a plotline
· Listen — the chapter read aloud

HEAR IT, AND MAKE THE AUDIOBOOK
Choose a narrator and give each character a voice of their own, from Google's natural voices (with a Google key of your own). The AI marks who says each line of dialogue; you change any it gets wrong. Play from any paragraph and follow along as it is read. When a chapter sounds right, approve it for the audiobook. A pronunciation list, drafted by the AI and heard before you keep it, makes the voices say your invented names your way. Then export the audiobook: an MP3 for every chapter at the standard the audiobook stores ask for, opening and closing credits, and one M4B file with chapter marks. You see what it will cost before anything is made, and nothing heard once is paid for again.
```

**Product features** — replace the last two of the 18 with these four (20 is the limit):

```
Six editor modes: Write, Create, Analysis, Characters, Plotlines and Listen
The AI writes at the place you choose, in the style you describe; you read it before it goes in
Hear a chapter read aloud: a narrator and a voice for each character
Export an audiobook: MP3 chapters at the stores' standard and one M4B with chapter marks
```

(So "Back the book … to a private GitHub repository" and "Take the whole book … to another computer" make way; keep them instead
of the two AI-summary lines if you prefer.)

**Search terms** — consider replacing "chapter editor" with `audiobook maker`.

**Screenshots** — `store\screenshots-1.1\` (1440 × 900, the sample book), to put **first**, before 1.0's seven:
01 Listen — the chapter read aloud · 03 Plotlines — key moments in the text · 04 Create — the AI writes at a + ·
05 Characters — in this chapter · 06 Analysis — a paragraph · 08 Pronunciations. (Two more — the voices panel and the
export window — showed a test folder in the status bar and were left out; retake them from `The Keeper of Gull Point` in your
library if you want them.)

**Notes for certification — add this paragraph:**

```
Testing the voices (new in 1.1). Listen mode, the pronunciations and the audiobook export use Google Cloud Text-to-Speech with the user's own Google key (an API key or a service-account file), entered under AI › AI Settings › Voices — Google text-to-speech. Without one, Listen mode explains that a key is needed and nothing is sent; every other feature works. The words being spoken go from the user's computer straight to texttospeech.googleapis.com when the user presses Play, hears a voice, approves a chapter or exports the audiobook; the cost is shown first. The audio is kept in the book's own folder. The audiobook is encoded with Windows' own Media Foundation encoders (MP3 and AAC); on a Windows "N" edition without the Media Feature Pack the export stops with a message, and nothing else is affected.
```

---

## 1. Where this stands

| | Item | State |
|---|---|---|
| ☑ | Store name **Author+** reserved in Partner Center | done by Ron |
| ☑ | Package identity name copied out of Partner Center | `DeanEaglin.Author` (Ron, 2026-11-05) — §2 |
| ☑ | `packaging/AppxManifest.xml` — identity, tiles, capabilities | in the repo |
| ☑ | `packaging/pack.ps1` — publish → stage → makepri → makeappx → sign | in the repo |
| ☑ | `packaging/make-msixupload.ps1` — the submission package | in the repo |
| ☑ | `packaging/run-wack.ps1` — certification kit with a readable summary | in the repo |
| ☑ | `packaging/make-store-assets.ps1` — every tile and listing image, drawn from one mark | in the repo |
| ☑ | Store assets generated — 52 files in `packaging/Assets`, 5 listing images in `store/images` | none over the size cap |
| ☑ | Application icon `src/AuthorPlus.App/AuthorPlus.ico` | wired into the csproj |
| ☑ | `src/AuthorPlus.App/app.manifest` — PerMonitorV2, asInvoker | required by WACK |
| ☑ | A real `.msixupload` builds end to end (71.8 MB at 0.10.0) | proven, dev identity |
| ☑ | Privacy policy written — `PRIVACY_POLICY.md`, and the page in the PunchMonkeyServer repo | §4 |
| ☑ | Privacy page live at https://softwareplus.ai/author/privacy/ | updated 2026-11-05 for Export/Import Project and Check for a new version — §4 |
| ☑ | Release version set to 1.0.0 | 2026-11-05 — §5 |
| ☑ | Submission package built with the real identity | `artifacts\AuthorPlus_1.0.0.0_x64_bundle.msixupload` (79.9 MB), family name checked, 2026-11-05 — §6 |
| ☐ | WACK run and passing | §7 |
| ☐ | Screenshots captured | §8 |
| ☐ | Partner Center submission completed | §9 |

---

## 2. The identity, and why it matters

Three values must match Partner Center exactly. Two are already known because they belong to the
account, not the app. **Package identity cannot be changed after the product is created**, so get
this right before anything else.

| Field | Value | Where it lives |
|---|---|---|
| `Package/Identity/Name` | **`DeanEaglin.Author`** (from Partner Center, 2026-11-05) | passed to the build as `-IdentityName` |
| `Package/Identity/Publisher` | `CN=E88392BA-A722-4B3A-8372-04403A55AA63` | passed as `-Publisher`; account-wide, same as SMADA and Statistle |
| `PublisherDisplayName` | `Dean Eaglin` | already in `packaging/AppxManifest.xml` |
| Package family name | **`DeanEaglin.Author_xs303fgqvwdg8`** | `-VerifyFamilyName` derives the same (checked 2026-11-05) |
| Store ID | **9MZCBQVSSDVG** | https://apps.microsoft.com/detail/9MZCBQVSSDVG — in Help › About and Check for a new version |
| Partner Center product | https://partner.microsoft.com/en-us/dashboard/products/9MZCBQVSSDVG/overview | |

The build can prove the identity before you upload: `make-msixupload.ps1 -VerifyFamilyName` derives
the package family name from the name and publisher and prints it. It must equal what Partner
Center shows. The derivation was checked against SMADA's known family name, so a mismatch means a
typo in your values, not in the script.

---

## 3. The package identity name (done 2026-11-05 — kept for the record)

The reserved name "Author+" is the *display* name. Partner Center separately issues a *package
identity name*, which is what goes in the manifest. It looks like `DeanEaglin.AuthorPlus` but it
is whatever Partner Center says, and a plus sign is not valid in it.

1. Go to https://partner.microsoft.com/dashboard/apps-and-games/overview and open **Author+**.
2. Left menu → **Product management** → **Product identity**.
3. Copy the value shown as **Package/Identity/Name**.
4. Also note **Package/Identity/Publisher** and confirm it is
   `CN=E88392BA-A722-4B3A-8372-04403A55AA63`, and copy the **Package family name** shown there.
5. Write both into the table in §2 of this file, so the next build does not need the dashboard.

If the product does not exist yet, create it: **New product → MSIX or PWA app** (not *Game*, and
not the MSI/EXE type — that one does not accept a `.msixupload`), type **Author+**, **Check
availability**, **Reserve product name**. A reservation is held for three months.

---

## 4. Publish the privacy policy

**Now on SoftwarePlus.ai** (the portfolio rule since 2026-09-30): https://softwareplus.ai/author/privacy/ — edit
`..\SoftwarePlus\site\author\privacy\index.html` and publish with `.\deploy.ps1 -Only author -Reason "…"` in that repo;
`PRIVACY_POLICY.md` here mirrors it. The PunchMonkeyServer route below is the old address, kept working for now.

The Store requires a working privacy policy URL before it will certify an app that touches the
network, and Author+ does when you use an AI feature. The page is written and lives in the
PunchMonkeyServer repo:

- `Components/Pages/Privacy/PrivacyAuthorPlus.razor` — the page, at `/privacy/authorplus`
- `Components/Pages/Privacy/PrivacyIndex.razor` — updated to list Author+ and to stop claiming
  that every Windows app of yours is offline, which is no longer true
- `PRIVACY_POLICY.md` in this repo mirrors the text

It builds locally. To publish it:

```powershell
cd C:\Users\ronal\source\repos\PunchMonkeyServer
.\deploy.ps1
```

That publishes, copies to the server over ssh, and restarts the `punchmonkey` service. Note that
it deploys **everything** currently in that repo, including the SMADA and Statistle privacy pages
that are still uncommitted there — look at `git status` first if that matters.

Then check both pages load:

- https://punchmonkeyserver.com/privacy/authorplus
- https://punchmonkeyserver.com/privacy

☐ Deployed and both URLs load.

---

## 5. Set the release version

`Directory.Build.props` holds one property that drives the assembly, the file version and the MSIX
package version:

```xml
<AuthorPlusVersion>1.0.0</AuthorPlusVersion>
```

Set it to `1.0.0` for the first Store release. The packaging scripts pad it to four parts and force
the fourth to `0`, which the Store reserves, so the package version becomes `1.0.0.0`.

Every later submission needs a **higher** version; the Store rejects a repeat. Bump the third part
for a fix, the second for features.

☐ Version set, `dotnet build AuthorPlus.sln` clean, `dotnet test tests/AuthorPlus.Tests` green.

---

## 6. Build the submission package

Prerequisites, all present on this machine: .NET SDK 10.0.400, Windows SDK 10.0.26100 (makeappx,
makepri, signtool), ImageMagick 7 (only for the assets), and the local NuGet feed `C:\nuget-local`
holding `Eaglin.AiManager` 1.2.0 — run `..\AiManager\build\pack.ps1` once if it is missing.

Regenerate the art only if it changed:

```powershell
cd C:\Users\ronal\source\repos\AuthorPlus
.\packaging\make-store-assets.ps1
```

Then build the package, with the identity from §3:

```powershell
.\packaging\make-msixupload.ps1 `
    -IdentityName '<the name from Partner Center>' `
    -Publisher    'CN=E88392BA-A722-4B3A-8372-04403A55AA63' `
    -VerifyFamilyName
```

It prints the derived package family name — **check it matches Partner Center before uploading** —
and writes three files to `artifacts\`:

| File | What it is |
|---|---|
| `AuthorPlus_1.0.0.0_x64_bundle.msixupload` | **this is what you upload** |
| `AuthorPlus_1.0.0.0_x64.msixbundle` | inside the upload; what WACK tests |
| `AuthorPlus_1.0.0.0_x64.appxsym` | symbols, inside the upload, for crash reports |

The package is deliberately **unsigned**. The Store re-signs every submission with its own
certificate and rejects packages signed with a local one. `pack.ps1` on its own does sign, with a
self-signed development certificate, for testing an install locally — that is a different job.

☐ Built, family name verified.

---

## 7. Run the certification kit

```powershell
.\packaging\run-wack.ps1
```

It elevates itself, tests the newest bundle in `artifacts\`, and prints a pass/fail summary instead
of leaving an XML file to read. It drives the app on screen for several minutes; leave the machine
alone while it does.

WACK is not a gate — Store certification runs its own — but a local pass avoids a failed round
trip. What to expect, from SMADA and Statistle, which are built the same way:

- **OVERALL PASS with 23 of 24**, the one failure being **Blocked executables**, which is optional.
  Almost all of its messages come from inside the .NET runtime that a self-contained app must
  carry. Expect a substring false positive too: any DLL whose name contains *reg* is reported as a
  reference to the blocked `reg` command.
- **App resources** is real: any logo image over 204,800 bytes fails it. The asset script keeps
  every tile under that automatically, so this only bites if art is added by hand.
- **DPIAwarenessValidation** is the one required test that a WPF app fails without an
  `app.manifest` declaring PerMonitorV2. Author+ has one, and the project suppresses the WinForms
  analyser that would otherwise refuse to let it compile.

☐ WACK run, overall PASS, no required test failing.

---

## 8. Screenshots

Partner Center wants PNGs **at least 1366 × 768**, up to ten, four recommended. The first is what
appears in search results, so lead with the tree and a chapter open.

SMADA's first set was rejected for being 1341–1347 px wide. Capture on a 1920-wide display, and if
a shot comes out short, **pad** it to a uniform size on its own background colour rather than
upscaling, which softens the text.

How to take them:

1. Run `manual-test\AuthorPlus.exe` with *The Book of One* open.
2. Size the window to 1366 × 768 or larger, maximised on a 1920 display is easiest.
3. `Win` + `Shift` + `S`, or Alt+PrtScn for the active window, then save as PNG into
   `store\screenshots\`.

The shot list worth having:

| # | Screen | Why |
|---|---|---|
| 1 | Tree with a chapter open in the editor | says what the app is in one look |
| 2 | Characters page — cast table and the chapter chart | the thing no word processor does |
| 3 | Plotlines board with the coloured dots | the same, and it looks like nothing else |
| 4 | A chapter analysis with its aspect cards | shows the AI working as an editor |
| 5 | The Suggestions screen with a diff open | shows Apply / Mark / Ask again |
| 6 | The prompt console with a request in it | shows that you see what is sent |

☐ At least four captured, none under 1366 px wide.

---

## 9. Partner Center, field by field

Open the product → **Start new submission**, then work down the left menu.

### Packages

Upload `artifacts\AuthorPlus_1.0.0.0_x64_bundle.msixupload`. Wait for it to validate; errors here
are almost always an identity mismatch (§2) or a version already used (§5).

### Availability

- Markets: **All**
- Visibility: **Public**
- Schedule: **Publish as soon as it passes certification**
- Discoverability: *Make this product available and discoverable in the Store*

### Pricing

- Base price: **Free** (Ron, 2026-11-05: *"We are going to start with free."* The earlier plan was
  a paid app.)
- Free trial: your call. A trial is a good fit for a writing tool, but it is a code change
  (the app has no trial mode today), so for 1.0 the answer is **No free trial**.
- Sale pricing: none.

### Properties

- Category: **Productivity**, subcategory **Personal finance** does not fit — use
  **Productivity → Other**. (Education → Instructional tools is where SMADA and Statistle sit;
  Author+ is a tool for doing work, not for learning.)
- Privacy policy URL: `https://softwareplus.ai/author/privacy/`
- Website: `https://softwareplus.ai/author/`
- Support contact: `ron.eaglin@gmail.com`
- **Product declarations:**
  - Purchases outside the Microsoft commerce engine — **No**. The AI features need the user's own
    API key from a third party, which is not a purchase made in the app; if certification queries
    it, the note in §10 covers it.
  - Tested to meet accessibility guidelines — **leave unchecked**. It has not been.
  - Depends on non-Microsoft drivers or NT services — **No**
  - Can run on Windows in S mode — **No** (a `runFullTrust` desktop app cannot)
  - Contains cryptography — **No** (DPAPI and TLS through the OS do not count as your own
    cryptography)
- System requirements: Windows 10 version 2004 (build 19041) or later, x64, about 250 MB disk,
  4 GB RAM. No special graphics.

### Age ratings

The IARC questionnaire. Every answer is negative, which gives **3+ / Everyone**:

| Question | Answer |
|---|---|
| Violence, fear, sexual content, profanity, drugs, gambling | None |
| In-app purchases | None |
| User-to-user interaction, chat, content sharing between users | None |
| Collects or shares personal information | **No** — the app collects nothing and sends nothing to you |
| Shares location | No |
| Advertising | No |
| Unrestricted internet access | **No** — it reaches the AI provider the user configured, and GitHub if the user backs a book up there; not the open web |

### Store listing

Copy the text in §11 into the matching fields. Then upload the images:

| Partner Center slot | File |
|---|---|
| Screenshots (at least 4) | `store\screenshots\*.png` — seven at 1440 × 900 of the sample book, made by `tools/store-shots.ps1` |
| Store logo 300 × 300 | `store\images\StoreLogo-300x300.png` |
| Box art 1:1 (1080 × 1080) | `store\images\BoxArt-1080x1080.png` |
| Poster art 2:3 (720 × 1080) | `store\images\PosterArt-720x1080.png` |
| Super hero art (2400 × 1200) | `store\images\SuperHeroArt-2400x1200.png` |
| Hero / featured 16:9 (1920 × 1080) | `store\images\HeroImage-1920x1080.png` |

Do not upload `asset-contact-sheet.png`; it is a proof sheet for checking the art, not a listing
image.

### Submission options

Paste the certification notes from §10. Then **Submit to the Store**.

☑ Submitted for certification 2026-11-05 (free; publishes as soon as it passes). **Live 2026-11-06.**

---

## 10. Notes for the certification testers

Paste this into **Submission options → Notes for certification**. It answers the two things a
tester will otherwise flag: the restricted capability, and an AI feature that appears to do
nothing.

> **runFullTrust.** Author+ is a Win32 desktop application (WPF on .NET 10) distributed as an MSIX
> package. The `runFullTrust` restricted capability is required for any desktop application
> packaged this way. It is used only to run as an ordinary desktop process and to read and write
> the user's own files — the book folders under `Documents\AuthorPlus\Books`, and the shared AI
> settings under `Documents\AiManager`. The app does not access other applications' data, requires
> no administrator rights, and installs no drivers or services.
>
> **internetClient.** Used for two purposes, both of them started by the user and both to a service
> the user signed in to themselves: sending a request to the AI provider whose API key the user
> entered, and — if the user chooses to back a book up — sending that book's folder to their own
> GitHub repository (`api.github.com` to check the sign-in and create the repository,
> `github.com` over HTTPS for the backup itself). The only other connection is to the Microsoft Store
> itself, when the user chooses Help › Check for a new version. The app sends no telemetry.
>
> **Testing the GitHub backup.** It needs a GitHub account and a personal access token of the
> tester's own, under Versions › Back Up to GitHub…. Without one, every other part of versioning —
> saving versions, reading the history, comparing two drafts of a chapter, putting the book back —
> works with no network connection at all.
>
> **Testing the AI features.** Author+ has no bundled AI subscription; the user supplies their own
> API key for Claude, Gemini, OpenAI, Mistral or xAI, under AI › AI Settings. Without a key the AI
> menu items explain that a key is needed and nothing is sent, which is the expected behaviour and
> not a failure. Everything else — creating a book, importing chapters, writing, characters,
> timeline, plotlines, export — works with no key and no network connection. To exercise an AI
> feature you would need to supply a key of your own; we can provide a temporary key on request.
>
> **Privacy policy:** https://softwareplus.ai/author/privacy/

---

## 11. Listing copy, ready to paste

Character limits are Partner Center's.

**Product name** [256]

```
Author+
```

**Short title** [50] — shown where space is tight

```
Author+
```

**Short description** [1000] — the line under the title in search

```
Write a book and keep it straight. Author+ holds your chapters, characters, timeline and plotlines in one place, and puts an editor beside you: it summarizes a chapter, reads it for pacing or tension, and suggests rewrites of the passage you choose — in your voice, guided by what you say the passage is for. Your book stays on your computer, and the AI features use your own API key.
```

**Description** [10000]

```
A book is more than the sentence in front of you. Author+ is for everything around it: the chapter you are writing, the twelve you have written, the character whose eyes changed colour in part two, and the thread you opened in chapter one and have not closed yet.

WRITE
Chapters in a proper editor, with headings, scene breaks, find and replace, a word count and a goal, and a distraction-free mode when you want the page and nothing else. Each chapter carries a status from outline to final, so you can see what is done. Import the chapters you have already written from Word, text or Markdown files; your originals are never touched. When it is ready to leave, export the whole book as a Word document, a PDF typeset as a book, an EPUB e-book or Markdown. Writing on a laptop as well? Export Project puts the whole book, with every saved version, in one file to carry there and back; Author+ tells you which copy is newer and never deletes the one it replaces.

KEEP IT STRAIGHT
Characters with the things that actually matter — motivations, what they do, how they change — and a chart that shows, at a glance, which chapter each character is introduced in, where they appear, and where they leave the story. A timeline that stays in story order even when the chapters do not. Plotlines with the same chart: where each thread is introduced, where it runs, and where it resolves.

AN EDITOR BESIDE YOU
The AI features are there to help you land what you were reaching for, not to write for you:
· Summarize a chapter into what happens and who does what
· Analyse one for pacing, tension, stakes, point of view, dialogue, continuity or prose habits
· Ask for rewrites of a passage you select, then say what the passage is for — the mood, the joke, the dread — and ask again until it is right
· Apply a suggestion into the chapter, mark it to rewrite yourself, or dismiss it
· Find the plotlines and the characters a chapter introduces, and add them to the book
Everything the AI produces is saved beside the chapter with the date, the provider and the model, so you can compare two opinions and keep the one that helps.

YOUR WORK, YOUR MACHINE, YOUR KEY
Your book is a folder of small files on your own computer — one per chapter, character, event and plotline — that you can read, copy and back up yourself. There are no accounts and the developer receives nothing. Nothing leaves your computer unless you ask for it: an AI request with your own key, or a backup to your own private GitHub repository.

The AI features need an API key of your own from Anthropic (Claude), Google (Gemini), OpenAI, Mistral or xAI. Requests go straight from your computer to that provider; you can see the exact text before it is sent, and the app shows you the tokens and estimated cost of every call. Without a key, Author+ is a complete offline writing tool.
```

**Product features** [up to 20, 200 each]

```
Chapters, characters, timeline and plotlines in one tree
A real editor: headings, scene breaks, find and replace, word goals, distraction-free mode
Import chapters you have already written from Word, text or Markdown
Export the whole book as Word, PDF, EPUB or Markdown
A chart of which chapter each character is introduced in, appears in, and leaves
The same chart for plotlines: introduced, continuing, resolved
AI chapter summaries, in bullets rather than a wall of text
AI analysis for pacing, tension, stakes, point of view, dialogue, continuity and prose habits
Rewrite suggestions for a passage you choose, shown as a diff you can apply with one click
Tell the AI what a passage is for and ask again until it lands
Bring your own API key: Claude, Gemini, OpenAI, Mistral or xAI
See the exact prompt, its token count and its cost before anything is sent
Your book is a folder of plain files on your computer; nothing is uploaded unless you ask
Every version of your book remembered, with what changed in each said in plain English
Read an old draft of a chapter against today's, and take back the one you preferred
Put the whole book back as it was on any day, without losing what came after
Back the book and its whole history up to a private GitHub repository of your own
Take the whole book, with every version, to another computer and back as one file
```

**Search terms** [up to 7, 30 each]

```
novel writing software
book writing app
manuscript organizer
plot and character tracker
writing with AI
story bible
chapter editor
```

**What's new in this version** [1500]

```
First release.
```

**Copyright and trademark info** [200]

```
© 2026 Ron Eaglin. All rights reserved.
```

**Developed by** [255]

```
Ron Eaglin
```

**Additional license terms** — leave as the Standard Application License Terms unless you want to
add the wording SMADA and Statistle use; there is no Author+-specific term that needs adding.

---

## 12. After it is live

☑ Tag the commit: `git tag v1.0.0 && git push --tags` (2026-11-06)
☑ Record the Store ID and the live URL in this file, under §2 (9MZCBQVSSDVG, https://apps.microsoft.com/detail/9MZCBQVSSDVG)
☑ Add Author+ to the Windows apps list in `..\repos\CLAUDE.md`
☑ Note the release in `docs/DEVELOPMENT-PLAN.md`, Phase 4 (4.7)
☑ softwareplus.ai: the Author+ page's "Coming soon" note replaced by the Store badge, facts "On the Microsoft Store · Version 1.0",
  the home page tile "Free on the Store"; deployed with `deploy.ps1 -Only author,index.html` (2026-11-06)

Certification usually takes a few hours to a couple of days. A rejection comes back with the policy
number; the two most likely for this app are **10.1** (the app must work as described — covered by
the certification notes in §10 explaining the API key) and **10.5.1** (a working privacy policy URL
— covered by §4).
