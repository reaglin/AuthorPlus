# Author+ — Microsoft Store submission

Everything needed to put **Author+** in the Microsoft Store, in the order to do it, with the exact
values to type and the exact commands to run. Tick each box as you go. Nothing here is guesswork
except the two items marked **NEEDS YOU**, which only exist in Partner Center.

Publisher account: sign-in `ron.eaglin@gmail.com`, publishing as **Dean Eaglin**
(https://apps.microsoft.com/search/publisher?name=Dean+Eaglin). Same account as SMADA and
Statistle, so most of the identity is already known.

---

## 1. Where this stands

| | Item | State |
|---|---|---|
| ☑ | Store name **Author+** reserved in Partner Center | done by Ron |
| ☐ | Package identity name copied out of Partner Center | **NEEDS YOU** — §3 |
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
| ☐ | Privacy page deployed to punchmonkeyserver.com | §4 — one command |
| ☐ | Release version set to 1.0.0 | §5 |
| ☐ | Submission package built with the real identity | §6 |
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
| `Package/Identity/Name` | **NEEDS YOU** — see §3 | passed to the build as `-IdentityName` |
| `Package/Identity/Publisher` | `CN=E88392BA-A722-4B3A-8372-04403A55AA63` | passed as `-Publisher`; account-wide, same as SMADA and Statistle |
| `PublisherDisplayName` | `Dean Eaglin` | already in `packaging/AppxManifest.xml` |
| Package family name | `<identity name>_xs303fgqvwdg8` | the `_xs303fgqvwdg8` hash is account-wide too |

The build can prove the identity before you upload: `make-msixupload.ps1 -VerifyFamilyName` derives
the package family name from the name and publisher and prints it. It must equal what Partner
Center shows. The derivation was checked against SMADA's known family name, so a mismatch means a
typo in your values, not in the script.

---

## 3. NEEDS YOU: the package identity name

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

- Base price: **Paid** — Author+ is a paid app, unlike SMADA and Statistle. Pick the tier, and
  keep in mind the Store shows tax-inclusive prices in some markets.
- Free trial: your call. A trial is a good fit for a writing tool, but it is a code change
  (the app has no trial mode today), so for 1.0 the answer is **No free trial**.
- Sale pricing: none.

### Properties

- Category: **Productivity**, subcategory **Personal finance** does not fit — use
  **Productivity → Other**. (Education → Instructional tools is where SMADA and Statistle sit;
  Author+ is a tool for doing work, not for learning.)
- Privacy policy URL: `https://punchmonkeyserver.com/privacy/authorplus`
- Website: `https://punchmonkeyserver.com`
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
| Unrestricted internet access | **No** — it reaches one API the user configured, not the open web |

### Store listing

Copy the text in §11 into the matching fields. Then upload the images:

| Partner Center slot | File |
|---|---|
| Screenshots (at least 4) | `store\screenshots\*.png` |
| Store logo 300 × 300 | `store\images\StoreLogo-300x300.png` |
| Box art 1:1 (1080 × 1080) | `store\images\BoxArt-1080x1080.png` |
| Poster art 2:3 (720 × 1080) | `store\images\PosterArt-720x1080.png` |
| Super hero art (2400 × 1200) | `store\images\SuperHeroArt-2400x1200.png` |
| Hero / featured 16:9 (1920 × 1080) | `store\images\HeroImage-1920x1080.png` |

Do not upload `asset-contact-sheet.png`; it is a proof sheet for checking the art, not a listing
image.

### Submission options

Paste the certification notes from §10. Then **Submit to the Store**.

☐ Submitted.

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
> **internetClient.** Used for one purpose: sending a request to the AI provider whose API key the
> user entered. The app contacts no other server, checks for no updates, and sends no telemetry.
>
> **Testing the AI features.** Author+ has no bundled AI subscription; the user supplies their own
> API key for Claude, Gemini, OpenAI, Mistral or xAI, under AI › AI Settings. Without a key the AI
> menu items explain that a key is needed and nothing is sent, which is the expected behaviour and
> not a failure. Everything else — creating a book, importing chapters, writing, characters,
> timeline, plotlines, export — works with no key and no network connection. To exercise an AI
> feature you would need to supply a key of your own; we can provide a temporary key on request.
>
> **Privacy policy:** https://punchmonkeyserver.com/privacy/authorplus

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
Chapters in a proper editor, with headings, scene breaks, find and replace, a word count and a goal, and a distraction-free mode when you want the page and nothing else. Each chapter carries a status from outline to final, so you can see what is done. Import the chapters you have already written from Word, text or Markdown files; your originals are never touched.

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
Your book is a folder of small files on your own computer — one per chapter, character, event and plotline — that you can read, copy and back up yourself. Nothing is uploaded, there are no accounts, and the developer receives nothing.

The AI features need an API key of your own from Anthropic (Claude), Google (Gemini), OpenAI, Mistral or xAI. Requests go straight from your computer to that provider; you can see the exact text before it is sent, and the app shows you the tokens and estimated cost of every call. Without a key, Author+ is a complete offline writing tool.
```

**Product features** [up to 20, 200 each]

```
Chapters, characters, timeline and plotlines in one tree
A real editor: headings, scene breaks, find and replace, word goals, distraction-free mode
Import chapters you have already written from Word, text or Markdown
A chart of which chapter each character is introduced in, appears in, and leaves
The same chart for plotlines: introduced, continuing, resolved
AI chapter summaries, in bullets rather than a wall of text
AI analysis for pacing, tension, stakes, point of view, dialogue, continuity and prose habits
Rewrite suggestions for a passage you choose, shown as a diff you can apply with one click
Tell the AI what a passage is for and ask again until it lands
Bring your own API key: Claude, Gemini, OpenAI, Mistral or xAI
See the exact prompt, its token count and its cost before anything is sent
Your book is a folder of plain files on your computer; nothing is uploaded
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

☐ Tag the commit: `git tag v1.0.0 && git push --tags`
☐ Record the Store ID and the live URL in this file, under §2
☐ Add Author+ to the Windows apps list in `..\repos\CLAUDE.md`
☐ Note the release in `docs/DEVELOPMENT-PLAN.md`, Phase 4

Certification usually takes a few hours to a couple of days. A rejection comes back with the policy
number; the two most likely for this app are **10.1** (the app must work as described — covered by
the certification notes in §10 explaining the API key) and **10.5.1** (a working privacy policy URL
— covered by §4).
