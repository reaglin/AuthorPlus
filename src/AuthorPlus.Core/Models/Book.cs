﻿using System.Text.Json.Serialization;

namespace AuthorPlus.Core.Models;

/// <summary>
/// A book is a folder on disk (see <see cref="Services.BookStore"/>). This object is the in-memory
/// view of that folder: metadata from <c>book.json</c> plus the collections the tree view
/// organises — sections, chapters, items, characters, timeline events and plotlines — each
/// item its own file.
///
/// Chapter prose is deliberately NOT on <see cref="Chapter"/>: it is a FlowDocument XAML file
/// beside the chapter's metadata, loaded and saved on demand so a 70-chapter book does not keep
/// every manuscript page in memory (and so a crash mid-save can only ever damage one chapter).
/// </summary>
public sealed class Book
{
    public const int CurrentFormatVersion = 3;

    public Guid     Id          { get; set; } = Guid.NewGuid();
    public string   Title       { get; set; } = "Untitled";
    public string   Author      { get; set; } = string.Empty;
    public string   Synopsis    { get; set; } = string.Empty;
    public string   Genre       { get; set; } = string.Empty;
    public int      TargetWords { get; set; }
    public DateTime CreatedUtc  { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Schema version of the folder layout; bump when the on-disk shape changes. 2 = sections + items.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Parts of the book, in reading order. A book may have none (chapters then sit at book level).</summary>
    public List<Section>       Sections   { get; set; } = new();

    /// <summary>Every chapter, in reading order across the whole book. <see cref="Chapter.SectionId"/> groups them.</summary>
    public List<Chapter>       Chapters   { get; set; } = new();

    /// <summary>Per-chapter / per-section / per-book items (summaries, analyses, notes…). Order is display order within an owner.</summary>
    public List<Item>          Items      { get; set; } = new();

    public List<Character>     Characters { get; set; } = new();
    public List<TimelineEvent> Timeline   { get; set; } = new();
    public List<Plotline>      Plotlines  { get; set; } = new();

    /// <summary>Absolute folder the book was loaded from / saved to. Not serialised.</summary>
    [JsonIgnore] public string FolderPath { get; set; } = string.Empty;

    [JsonIgnore] public int TotalWords => Chapters.Sum(c => c.WordCount);

    // ── Navigation helpers (display-time only; nothing here is serialised) ────

    public IEnumerable<Chapter> ChaptersOf(Section section) => Chapters.Where(c => c.SectionId == section.Id);

    /// <summary>Chapters that belong to no section (the whole book when it has no parts).</summary>
    public IEnumerable<Chapter> UnsectionedChapters => Chapters.Where(c => c.SectionId is null || Sections.All(s => s.Id != c.SectionId));

    public Section? SectionOf(Chapter chapter) => chapter.SectionId is { } id ? Sections.FirstOrDefault(s => s.Id == id) : null;

    public IEnumerable<Item> ItemsOf(Guid ownerId) => Items.Where(i => i.OwnerId == ownerId);

    /// <summary>The chapter's one Summary item, or null.</summary>
    public Item? SummaryOf(Chapter chapter) => Items.FirstOrDefault(i => i.OwnerId == chapter.Id && i.Kind == ItemKind.Summary);

    /// <summary>The summary text used by prompts and views: the Summary item's body, or "".</summary>
    public string SummaryText(Chapter chapter) => SummaryOf(chapter)?.Body ?? string.Empty;

    /// <summary>Creates or refreshes the chapter's single Summary item.</summary>
    public Item SetSummary(Chapter chapter, string text, string? provider = null, string? model = null, string? promptName = null)
    {
        var item = SummaryOf(chapter);
        if (item is null)
        {
            item = new Item { OwnerId = chapter.Id, Kind = ItemKind.Summary, Title = "Summary" };
            Items.Add(item);
        }
        item.Body = text;
        item.Provider = provider;
        item.Model = model;
        item.PromptName = promptName;
        item.ModifiedUtc = DateTime.UtcNow;
        return item;
    }

    // ── Plotlines: what a thread does in each chapter ─────────────────────────

    /// <summary>
    /// Gives every chapter the thread runs through a part to play, without changing one already
    /// set: the first chapter in reading order introduces it, the rest continue it. Run on load
    /// and whenever chapters are linked from somewhere that does not say more.
    /// </summary>
    public void SeedRoles(Plotline plotline)
    {
        plotline.ChapterRoles.RemoveAll(r => !plotline.ChapterIds.Contains(r.ChapterId));
        var inOrder = Chapters.Where(c => plotline.ChapterIds.Contains(c.Id)).Select(c => c.Id).ToList();
        for (int i = 0; i < inOrder.Count; i++)
        {
            if (plotline.ChapterRoles.Any(r => r.ChapterId == inOrder[i])) continue;
            plotline.ChapterRoles.Add(new ChapterRole { ChapterId = inOrder[i], Role = i == 0 ? PlotlineRole.Introduced : PlotlineRole.Continuing });
        }
        RecomputeStatus(plotline);
    }

    /// <summary>
    /// Sets what a thread does in one chapter, or takes it out of the chapter when
    /// <paramref name="role"/> is null. Keeps <see cref="Plotline.ChapterIds"/> and the thread's
    /// overall status in step, so the board, the tables and the tree never disagree.
    /// </summary>
    public void SetRole(Plotline plotline, Guid chapterId, PlotlineRole? role)
    {
        plotline.ChapterRoles.RemoveAll(r => r.ChapterId == chapterId);
        if (role is { } r2)
        {
            plotline.ChapterRoles.Add(new ChapterRole { ChapterId = chapterId, Role = r2 });
            if (!plotline.ChapterIds.Contains(chapterId)) plotline.ChapterIds.Add(chapterId);
        }
        else plotline.ChapterIds.Remove(chapterId);
        RecomputeStatus(plotline);
    }

    /// <summary>
    /// The thread's status follows its chapters: resolved once a chapter resolves it, active while
    /// it runs anywhere, planned while it runs nowhere yet.
    /// </summary>
    public void RecomputeStatus(Plotline plotline) =>
        plotline.Status =
            plotline.ChapterRoles.Any(r => r.Role == PlotlineRole.Resolved) ? PlotlineStatus.Resolved
            : plotline.ChapterIds.Count > 0                                 ? PlotlineStatus.Active
                                                                            : PlotlineStatus.Planned;

    /// <summary>
    /// The thread's life in chapter numbers, as a sentence: "Introduced ch. 1, continuing through
    /// ch. 23, resolved ch. 25". Empty when it runs through no chapter yet.
    /// </summary>
    public string RoleSpan(Plotline plotline)
    {
        var runs = Chapters.Where(c => plotline.ChapterIds.Contains(c.Id)).ToList();
        if (runs.Count == 0) return "";
        string Num(Chapter c) => $"ch. {ChapterNumber(c)}";
        var introduced = runs.FirstOrDefault(c => plotline.RoleIn(c.Id) == PlotlineRole.Introduced) ?? runs[0];
        var resolved = runs.LastOrDefault(c => plotline.RoleIn(c.Id) == PlotlineRole.Resolved);
        var last = runs[^1];

        var parts = new List<string> { $"Introduced {Num(introduced)}" };
        if (runs.Count > 1 && (resolved is null || resolved.Id != last.Id || runs.Count > 2))
            parts.Add($"continuing through {Num(resolved is not null && resolved.Id == last.Id && runs.Count > 1 ? runs[^2] : last)}");
        if (resolved is not null) parts.Add($"resolved {Num(resolved)}");
        return string.Join(", ", parts) + $"  ({runs.Count} chapter{(runs.Count == 1 ? "" : "s")})";
    }

    // ── Characters: what they do in each chapter ──────────────────────────────

    /// <summary>
    /// Gives every chapter a character is in a part to play, without changing one already set: the
    /// first chapter in reading order introduces them, the rest are appearances. The chapters that
    /// list a character (<see cref="Chapter.CharacterIds"/>) stay the record of where they are.
    /// </summary>
    public void SeedRoles(Character character)
    {
        var inOrder = Chapters.Where(c => c.CharacterIds.Contains(character.Id)).Select(c => c.Id).ToList();
        character.ChapterRoles.RemoveAll(r => !inOrder.Contains(r.ChapterId));
        for (int i = 0; i < inOrder.Count; i++)
        {
            if (character.ChapterRoles.Any(r => r.ChapterId == inOrder[i])) continue;
            character.ChapterRoles.Add(new CharacterChapterRole { ChapterId = inOrder[i], Role = i == 0 ? CharacterRole.Introduced : CharacterRole.Appears });
        }
    }

    /// <summary>
    /// Sets what a character does in one chapter, or takes them out of it when
    /// <paramref name="role"/> is null (which also drops them as its point-of-view character).
    /// Keeps the chapter's own list in step, so the chart, the tables and the status bar agree.
    /// </summary>
    public void SetRole(Character character, Guid chapterId, CharacterRole? role)
    {
        var chapter = Chapters.FirstOrDefault(c => c.Id == chapterId);
        character.ChapterRoles.RemoveAll(r => r.ChapterId == chapterId);
        if (role is { } r2)
        {
            character.ChapterRoles.Add(new CharacterChapterRole { ChapterId = chapterId, Role = r2 });
            if (chapter is not null && !chapter.CharacterIds.Contains(character.Id)) chapter.CharacterIds.Add(character.Id);
        }
        else if (chapter is not null)
        {
            chapter.CharacterIds.Remove(character.Id);
            if (chapter.PovCharacterId == character.Id) chapter.PovCharacterId = null;
        }
    }

    /// <summary>
    /// The character's life in chapter numbers: "Introduced ch. 2, through ch. 40, leaves ch. 52".
    /// Empty when no chapter lists them yet.
    /// </summary>
    public string RoleSpan(Character character)
    {
        var inOrder = Chapters.Where(c => c.CharacterIds.Contains(character.Id)).ToList();
        if (inOrder.Count == 0) return "";
        string Num(Chapter c) => $"ch. {ChapterNumber(c)}";
        var introduced = inOrder.FirstOrDefault(c => character.RoleIn(c.Id) == CharacterRole.Introduced) ?? inOrder[0];
        var leaves = inOrder.LastOrDefault(c => character.RoleIn(c.Id) == CharacterRole.Leaves);

        var parts = new List<string> { $"Introduced {Num(introduced)}" };
        if (inOrder.Count > 1 && (leaves is null || leaves.Id != inOrder[^1].Id || inOrder.Count > 2))
            parts.Add($"through {Num(leaves is not null && leaves.Id == inOrder[^1].Id && inOrder.Count > 1 ? inOrder[^2] : inOrder[^1])}");
        if (leaves is not null) parts.Add($"leaves {Num(leaves)}");
        int pov = Chapters.Count(c => c.PovCharacterId == character.Id);
        return string.Join(", ", parts) + $"  ({inOrder.Count} chapter{(inOrder.Count == 1 ? "" : "s")}" + (pov > 0 ? $", {pov} told from their point of view)" : ")");
    }

    /// <summary>1-based position of a chapter within its section (or among the unsectioned chapters).</summary>
    public int ChapterNumber(Chapter chapter)
    {
        var siblings = chapter.SectionId is { } sid && Sections.Any(s => s.Id == sid)
            ? Chapters.Where(c => c.SectionId == sid)
            : UnsectionedChapters;
        int n = 0;
        foreach (var c in siblings) { n++; if (c.Id == chapter.Id) return n; }
        return 0;
    }

    /// <summary>Removes a chapter and everything that hangs off it (its items, and theirs). The caller saves.</summary>
    public void RemoveChapter(Chapter chapter)
    {
        Chapters.Remove(chapter);
        RemoveOwned(chapter.Id);
    }

    /// <summary>Removes an item and every item that hangs off it (e.g. an analysis and its suggestions).</summary>
    public void RemoveItemTree(Item item)
    {
        Items.Remove(item);
        RemoveOwned(item.Id);
    }

    /// <summary>Removes every item owned by <paramref name="ownerId"/>, recursively.</summary>
    public void RemoveOwned(Guid ownerId)
    {
        foreach (var child in Items.Where(i => i.OwnerId == ownerId).ToList())
        {
            Items.Remove(child);
            RemoveOwned(child.Id);
        }
    }

    /// <summary>Removes a section and its items. Its chapters stay in the book at book level unless <paramref name="deleteChapters"/>.</summary>
    public void RemoveSection(Section section, bool deleteChapters)
    {
        foreach (var c in ChaptersOf(section).ToList())
        {
            if (deleteChapters) RemoveChapter(c);
            else c.SectionId = null;
        }
        Sections.Remove(section);
        RemoveOwned(section.Id);
    }
}

/// <summary>A part of the book ("Part 1 — The Power of One"). Groups chapters; can carry items of its own.</summary>
public sealed class Section
{
    public Guid     Id          { get; set; } = Guid.NewGuid();
    public string   Title       { get; set; } = "New Section";
    public string   Summary     { get; set; } = string.Empty;
    public string   Notes       { get; set; } = string.Empty;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}

public enum ChapterStatus { Outline, Draft, Revised, Final }

/// <summary>Chapter metadata. The prose lives in <c>chapters/{Id}.xaml</c>.</summary>
public sealed class Chapter
{
    public Guid          Id             { get; set; } = Guid.NewGuid();
    public string        Title          { get; set; } = "New Chapter";
    /// <summary>The section this chapter belongs to; null = directly under the book.</summary>
    public Guid?         SectionId      { get; set; }
    public ChapterStatus Status         { get; set; } = ChapterStatus.Outline;
    public int           WordCount      { get; set; }
    /// <summary>Optional per-chapter goal; 0 = none. The book-level goal is <see cref="Book.TargetWords"/>.</summary>
    public int           TargetWords    { get; set; }
    public Guid?         PovCharacterId { get; set; }
    public List<Guid>    CharacterIds   { get; set; } = new();
    public List<Guid>    PlotlineIds    { get; set; } = new();
    public string        Notes          { get; set; } = string.Empty;
    public DateTime      ModifiedUtc    { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Format-1 books kept the summary here. On load it becomes a Summary <see cref="Item"/>
    /// and this is cleared, so it is never written again (null is omitted from JSON).
    /// </summary>
    [JsonPropertyName("Summary"), JsonInclude]
    internal string? LegacySummary { get; set; }
}

/// <summary>What kind of record an <see cref="Item"/> is. Serialised by name.</summary>
public enum ItemKind
{
    /// <summary>One per chapter: what happens, in a few sentences. AI-drafted, editable.</summary>
    Summary,
    /// <summary>An evaluation or critique. Kept as dated history; several per chapter.</summary>
    Analysis,
    /// <summary>Which characters appear in a chapter, with POV. One per chapter.</summary>
    CharactersInChapter,
    /// <summary>Free notes / to-do.</summary>
    Notes,
    /// <summary>A numbered chapter-flow outline (section or book level).</summary>
    Outline,
    /// <summary>Which plotlines run through a chapter, with what happens in each. One per chapter.</summary>
    ChapterPlotlines,
    /// <summary>Rewrite suggestions for one aspect of an analysis. Owned by the Analysis item.</summary>
    Suggestions
}

/// <summary>
/// A record attached to a chapter, a section or the book itself (<see cref="OwnerId"/>). The
/// tree shows items as children of their owner. AI-produced items remember which provider,
/// model and prompt template made them.
/// </summary>
public sealed class Item
{
    public Guid       Id          { get; set; } = Guid.NewGuid();
    public Guid       OwnerId     { get; set; }
    public ItemKind   Kind        { get; set; } = ItemKind.Notes;
    public string     Title       { get; set; } = string.Empty;
    public string     Body        { get; set; } = string.Empty;
    public DateTime   CreatedUtc  { get; set; } = DateTime.UtcNow;
    public DateTime   ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>For AI-made items: provider name ("Claude"), model id and the prompt template used.</summary>
    public string?    Provider    { get; set; }
    public string?    Model       { get; set; }
    public string?    PromptName  { get; set; }

    /// <summary>For <see cref="ItemKind.CharactersInChapter"/>: who appears and whose point of view it is.</summary>
    public List<Guid> CharacterIds   { get; set; } = new();
    public Guid?      PovCharacterId { get; set; }
    /// <summary>For <see cref="ItemKind.ChapterPlotlines"/>: the plotlines running through the chapter.</summary>
    public List<Guid> PlotlineIds    { get; set; } = new();

    /// <summary>For <see cref="ItemKind.Suggestions"/>: the structured suggestions (the Body keeps the raw reply).</summary>
    public List<SuggestionEntry> Suggestions { get; set; } = new();

    /// <summary>The author has dealt with this item (analysis, suggestions, notes…); the tree shows it ticked.</summary>
    public bool      Resolved    { get; set; }
    public DateTime? ResolvedUtc { get; set; }

    [JsonIgnore] public bool IsAiMade => !string.IsNullOrEmpty(Provider);
}

public enum SuggestionStatus
{
    /// <summary>Not acted on.</summary>
    Open,
    /// <summary>The rewrite was put into the chapter.</summary>
    Applied,
    /// <summary>Kept for the author to rewrite in their own words (see <see cref="SuggestionEntry.AuthorRewrite"/>).</summary>
    Marked,
    /// <summary>Dealt with, one way or another.</summary>
    Resolved
}

/// <summary>One rewrite suggestion: the passage as it is, the proposed text, why, and what the author did with it.</summary>
public sealed class SuggestionEntry
{
    public Guid             Id            { get; set; } = Guid.NewGuid();
    public int              Index         { get; set; }
    public string           Original      { get; set; } = string.Empty;
    public string           Rewrite       { get; set; } = string.Empty;
    public string           Why           { get; set; } = string.Empty;
    public SuggestionStatus Status        { get; set; } = SuggestionStatus.Open;
    /// <summary>The author's own version, written against a marked passage.</summary>
    public string           AuthorRewrite { get; set; } = string.Empty;
    public DateTime?        ActedUtc      { get; set; }

    /// <summary>When the author asked again: what they said they were trying to convey.</summary>
    public string           AuthorRequest { get; set; } = string.Empty;
    /// <summary>When the author asked again: the effect they were after ("menace, dry humour").</summary>
    public string           Intent        { get; set; } = string.Empty;
    /// <summary>The suggestion this one was asked to improve on, when it came from "Ask again".</summary>
    public Guid?            RefinesId     { get; set; }
    /// <summary>Which provider and model wrote this entry (a later "ask again" may use another).</summary>
    public string?          Provider      { get; set; }
    public string?          Model         { get; set; }

    [JsonIgnore] public bool IsRefinement => RefinesId is not null;
}

/// <summary>
/// A character: the basics, plus the material an author actually needs to keep straight —
/// motivations, what they do in the story, and how they change.
/// </summary>
public sealed class Character
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Name        { get; set; } = "New Character";
    public string Role        { get; set; } = string.Empty;   // protagonist, antagonist, supporting…
    public string Origin      { get; set; } = string.Empty;
    /// <summary>Appearance: build, face, voice, how they carry themselves. One entry per line (see <see cref="Services.Bullets"/>).</summary>
    public string PhysicalDescription { get; set; } = string.Empty;
    /// <summary>Temperament, manner, habits of mind — what they are like to be in a room with.</summary>
    public string Personality { get; set; } = string.Empty;
    public string Motivations { get; set; } = string.Empty;  // what they want, what they fear
    public string Actions     { get; set; } = string.Empty;  // what they do across the book
    public string Arc         { get; set; } = string.Empty;  // how they change
    public string Notes       { get; set; } = string.Empty;
    /// <summary>Other names the text uses for this character ("Lucy", "Detective Dalgo"), one per line.</summary>
    public string Aliases     { get; set; } = string.Empty;

    /// <summary>
    /// What the character does in each chapter they are in: they arrive, they are about, or they
    /// leave the story. Seeded from the chapters that list them.
    /// </summary>
    public List<CharacterChapterRole> ChapterRoles { get; set; } = new();

    /// <summary>Their part in a chapter, or null when they are not in it.</summary>
    public CharacterRole? RoleIn(Guid chapterId) =>
        ChapterRoles.FirstOrDefault(r => r.ChapterId == chapterId)?.Role;

    /// <summary>
    /// Format-2 books kept appearance and manner in one "Description". On load it moves into
    /// <see cref="PhysicalDescription"/> and this is cleared, so it is never written again.
    /// </summary>
    [JsonPropertyName("Description"), JsonInclude]
    internal string? LegacyDescription { get; set; }
}

/// <summary>
/// What a character does in one chapter: they arrive in the story here, they are about, or this is
/// where they leave it. The same three parts a plotline plays, in a character's terms — and, like a
/// plotline's, they are what the boards colour.
/// </summary>
public enum CharacterRole { Introduced, Appears, Leaves }

/// <summary>A character's part in one chapter. Absent = they are not in that chapter.</summary>
public sealed class CharacterChapterRole
{
    public Guid          ChapterId { get; set; }
    public CharacterRole Role      { get; set; } = CharacterRole.Appears;
}

/// <summary>
/// One entry on the book's linear timeline. Chapters need not follow story order, so events
/// carry their own <see cref="Order"/> and a free-form <see cref="When"/> ("Day 3, dawn";
/// "Spring 1847") rather than a calendar date.
/// </summary>
public sealed class TimelineEvent
{
    public Guid       Id           { get; set; } = Guid.NewGuid();
    public int        Order        { get; set; }
    public string     When         { get; set; } = string.Empty;
    public string     Title        { get; set; } = "New Event";
    public string     Description  { get; set; } = string.Empty;
    public List<Guid> ChapterIds   { get; set; } = new();
    public List<Guid> CharacterIds { get; set; } = new();
    public List<Guid> PlotlineIds  { get; set; } = new();
}

public enum PlotlineStatus { Planned, Active, Resolved }

/// <summary>How much of the book a thread carries: the spine, a thread beside it, or a side story.</summary>
/// <summary>
/// How much of the book a thread carries. <b>Primary</b> is a spine the book turns on;
/// <b>Secondary</b> runs beside it across chapters; <b>Chapter</b> opens and closes inside one
/// chapter; <b>Subplot</b> is a side story; <b>Extra</b> is a thread the author is watching that
/// carries no weight yet.
/// </summary>
public enum PlotlineKind { Primary, Secondary, Chapter, Subplot, Extra }

/// <summary>What a thread is doing in one chapter: it starts here, it runs on, or it ends here.</summary>
public enum PlotlineRole { Introduced, Continuing, Resolved }

/// <summary>A plotline's part in one chapter. Absent = the thread does not run through that chapter.</summary>
public sealed class ChapterRole
{
    public Guid         ChapterId { get; set; }
    public PlotlineRole Role      { get; set; } = PlotlineRole.Continuing;
}

/// <summary>A thread of the story. Plotlines converge; that is recorded on both sides.</summary>
public sealed class Plotline
{
    public Guid           Id           { get; set; } = Guid.NewGuid();
    public string         Name         { get; set; } = "New Plotline";
    /// <summary>What the thread is, one entry per line; the AI adds to it as later chapters say more.</summary>
    public string         Summary      { get; set; } = string.Empty;
    /// <summary>Planned while it is still an intention, Active once it runs, Resolved when a chapter closes it.</summary>
    public PlotlineStatus Status       { get; set; } = PlotlineStatus.Planned;
    /// <summary>Why it stands where it does: the plan while it is planned, how it plays out once it runs.</summary>
    public string         StatusNote   { get; set; } = string.Empty;
    public PlotlineKind   Kind         { get; set; } = PlotlineKind.Secondary;
    /// <summary>The chapters the thread runs through. <see cref="ChapterRoles"/> says what it does in each.</summary>
    public List<Guid>     ChapterIds   { get; set; } = new();
    /// <summary>Introduced / continuing / resolved, per chapter. Seeded from <see cref="ChapterIds"/> on load.</summary>
    public List<ChapterRole> ChapterRoles { get; set; } = new();
    public List<Guid>     CharacterIds { get; set; } = new();
    public List<PlotlineConvergence> Convergences { get; set; } = new();
    public string         Notes        { get; set; } = string.Empty;

    /// <summary>What the thread does in a chapter, or null when it does not run through it.</summary>
    public PlotlineRole? RoleIn(Guid chapterId) =>
        ChapterRoles.FirstOrDefault(r => r.ChapterId == chapterId)?.Role;
}

/// <summary>Where this plotline meets another one.</summary>
public sealed class PlotlineConvergence
{
    public Guid   OtherPlotlineId { get; set; }
    public Guid?  ChapterId       { get; set; }
    public string Note            { get; set; } = string.Empty;
}
