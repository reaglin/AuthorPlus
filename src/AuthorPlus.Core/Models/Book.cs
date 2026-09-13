using System.Text.Json.Serialization;

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
    public const int CurrentFormatVersion = 2;

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

    [JsonIgnore] public bool IsAiMade => !string.IsNullOrEmpty(Provider);
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
    public string Description { get; set; } = string.Empty;  // appearance, voice, mannerisms
    public string Motivations { get; set; } = string.Empty;  // what they want, what they fear
    public string Actions     { get; set; } = string.Empty;  // what they do across the book
    public string Arc         { get; set; } = string.Empty;  // how they change
    public string Notes       { get; set; } = string.Empty;
    /// <summary>Other names the text uses for this character ("Lucy", "Detective Dalgo"), one per line.</summary>
    public string Aliases     { get; set; } = string.Empty;
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
public enum PlotlineKind { Primary, Secondary, Subplot }

/// <summary>A thread of the story. Plotlines converge; that is recorded on both sides.</summary>
public sealed class Plotline
{
    public Guid           Id           { get; set; } = Guid.NewGuid();
    public string         Name         { get; set; } = "New Plotline";
    public string         Summary      { get; set; } = string.Empty;
    public PlotlineStatus Status       { get; set; } = PlotlineStatus.Planned;
    public PlotlineKind   Kind         { get; set; } = PlotlineKind.Secondary;
    public List<Guid>     ChapterIds   { get; set; } = new();
    public List<Guid>     CharacterIds { get; set; } = new();
    public List<PlotlineConvergence> Convergences { get; set; } = new();
    public string         Notes        { get; set; } = string.Empty;
}

/// <summary>Where this plotline meets another one.</summary>
public sealed class PlotlineConvergence
{
    public Guid   OtherPlotlineId { get; set; }
    public Guid?  ChapterId       { get; set; }
    public string Note            { get; set; } = string.Empty;
}
