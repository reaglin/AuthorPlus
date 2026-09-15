# Versions and GitHub

How AuthorPlus keeps the history of a book, and how a book gets a copy on GitHub.
Written 2026-09-15 with phase 5 (`DEVELOPMENT-PLAN.md`).

**Working against GitHub since 2026-09-15**: Ron signed in with a token, created the private
repository `github.com/reaglin/The_book_of_one` from inside the app, and pushed *The Book of One*
to it. The API calls also have tests against a stub transport (`GitHubApiTests`).

## What the author sees

A book folder can keep **versions**. A version is the whole book as it stood at one moment, with
a line saying what it is ("Chapter 12 — The Interrogation revised"). The `Versions` menu:

| Menu item | What it does |
|---|---|
| Save a Version… (Ctrl+Shift+S) | Shows what changed since the last one, offers a name for it, saves it |
| Version History… (Ctrl+H) | Every version, what each changed, and *Put the book back to this version* |
| This Chapter's History… | The drafts of the selected chapter: read one against today's, or take it back |
| Back Up to GitHub… | Sign in once (the window shows how to make a token), name the repository, and the book with its whole history goes there. Live even before a book keeps versions — it offers to start them as part of setting the backup up |
| Back Up Now / Get Changes from GitHub | The two everyday buttons afterwards |
| Save a version when I close the book | On by default once versioning starts |

The status bar says `12 versions · 3 not saved`; clicking it opens the history.

**The words git, commit, push, pull, branch, merge and remote appear nowhere in the UI.** They are
the mechanism, not the idea. The idea is: the book remembers where it has been, and GitHub holds
a copy.

## What that is underneath

| The book's word | git |
|---|---|
| Start keeping versions | `git init` on the book folder, branch `main`, a first commit of everything |
| A version | a commit (author = the book's author, from `Book.Author`) |
| Save a version | `add -A` + `commit` |
| Put the book back | hard reset to that commit, branch moved back to the tip, then commit — so the restore is a **new** version and nothing after it is lost |
| Back up | `push origin main` (HTTPS, the token as the password) |
| Get changes | `pull` — fast-forward or merge |
| Which copy to keep | force-push, or reset to `origin/main` after parking the local tip on a `kept-from-this-computer-…` branch |

`LibGit2Sharp` (MIT) rather than `git.exe`: a reader who buys AuthorPlus from the Store has no
Git installed. The native `git2-*.dll` comes with the package and is carried into the MSIX and
into the single-file hand-test exe (both verified 2026-09-15).

Everything lives in `Core/Services/Versioning/`:

| File | What |
|---|---|
| `BookRepository.cs` | The whole of git AuthorPlus uses. Synchronous — the App runs it off the UI thread |
| `ChangeNames.cs` | `chapters/6f3a….xaml` → "Chapter 12 — The Interrogation (the text)", and the suggested version name |
| `TextDiff.cs` | Two versions of a chapter compared paragraph by paragraph, in the prose, not in the XAML |
| `GitHubApi.cs` | Three calls: who is this token, does this repository exist, make one |
| `GitHubAccount.cs` | The token at rest in `Documents\AuthorPlus\github.json`, DPAPI-encrypted |
| `BookVersion.cs` | `BookVersion`, `BookChange`, `VersionAuthor`, `BookVersioningException` |

## Decisions worth remembering

- **A restore never erases.** Putting the book back to an older version adds a version whose
  content is the old one. Anything unsaved is committed first ("Work in progress, saved before
  restoring an older version"), so the morning's writing survives an impatient restore.
- **`progress.json` is not versioned.** It counts the words written today *on this computer*;
  versioning it would make two computers disagree about a number that is not part of the book.
  `.gitignore` also holds `*.tmp` (BookStore writes temp-then-move) and exported `.docx/.pdf/.epub`.
- **`.gitattributes` is `* -text`.** No end-of-line conversion: AuthorPlus writes every file and
  reads it back byte for byte, on any computer.
- **The repository is created empty on GitHub** (`auto_init: false`) — a README commit would stand
  in the way of the book's own first version.
- **Private by default.** The tick box says why: an unpublished manuscript on a public repository
  can be read, copied and indexed by anyone.
- **The token, not a password.** GitHub takes no password from an app. The window carries the
  five steps for making one (name it, choose how long it lasts, Generate token, copy it — GitHub
  shows it once — paste it here), links straight to the page that arrives with the `repo`
  permission already ticked, and notes the Tokens (classic) route for an account that is offered
  fine-grained tokens first. The token stays on this computer, encrypted for this Windows user
  (the same DPAPI scheme `Eaglin.AiManager` uses for API keys). One token serves every book.
- **Nothing in the Versions menu greys out without saying why.** Save a Version, Version History
  and Back Up to GitHub stay live for any open book and start keeping versions themselves if the
  book keeps none — asked in the terms of what was clicked, so choosing Back Up to GitHub asks
  whether to back *this book* up to GitHub and explains that versions are the thing GitHub holds,
  rather than telling the author to go and find another menu item first.
  Back Up Now and Get Changes do need a connected book, and their tooltip says which step is
  missing.
- **A clash asks a question rather than leaving conflict markers.** If the same chapter was
  written here and on another computer, the merge is undone, nothing on disk is touched, and the
  author picks which copy wins. Taking GitHub's copy parks what was here on a
  `kept-from-this-computer-…` branch first, so nothing is destroyed either way.
- **Versions are per book, not per library.** Each book folder is its own repository, so one book
  can be on GitHub and another not.

## Hand-testing it

1. Open a book › Versions › **Start Keeping Versions…** — the trilogy takes about two seconds.
2. Write a paragraph, save, then **Save a Version…** — the offered name should already say which
   chapter changed. The file list under it should read in chapter and character names, not GUIDs.
3. **Version History…** — pick the first version, look at *What this version changed*, then
   double-click a chapter line: the compare window shows the prose with the words that moved
   marked, not the XAML.
4. **Put the book back to this version…** on an older version: the tree reloads, the chapter is
   as it was, and the history has *grown* by two entries (work in progress, then the restore).
5. **This Chapter's History…** on a chapter you have rewritten, then *Put the chapter back to
   this version* — only that chapter changes.
6. **Back Up to GitHub…**: make a token, sign in, keep the suggested repository name, leave
   Private ticked, press *Create it and back up the book*. Open the link — the book folder is
   there. Press *Back up now* after another version; press *Get changes from GitHub* (it should
   say already up to date).
7. Close the book (or the app) with unsaved work: a version is saved by itself, and pushed if the
   book is connected. Untick *Save a version when I close the book* to stop that.

## What is not there

- No branches, no merges, no staging. If a book ever needs them, the folder is an ordinary git
  repository and any git client can be pointed at it.
- No cloning a book *from* GitHub onto a second computer inside the app: copy the folder, or
  `git clone` it, then File › Open Book. Worth adding when two machines are actually in use.
