using System.Text.Json.Serialization;

namespace AuthorPlus.Core.Models;

/// <summary>
/// A book is a folder on disk (see <see cref="Services.BookStore"/>). This object is the in-memory
/// view of that folder: metadata from <c>book.json</c> plus the four collections the tree view
/// organises — chapters, characters, timeline events and plotlines — each item its own file.
///
/// Chapter prose is deliberately NOT on <see cref="Chapter"/>: it is a FlowDocument XAML file
/// beside the chapter's metadata, loaded and saved on demand so a 40-chapter book does not keep
/// every manuscript page in memory (and so a crash mid-save can only ever damage one chapter).
/// </summary>
public sealed class Book
{
    public Guid     Id          { get; set; } = Guid.NewGuid();
    public string   Title       { get; set; } = "Untitled";
    public string   Author      { get; set; } = string.Empty;
    public string   Synopsis    { get; set; } = string.Empty;
    public string   Genre       { get; set; } = string.Empty;
    public int      TargetWords { get; set; }
    public DateTime CreatedUtc  { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Schema version of the folder layout; bump when the on-disk shape changes.</summary>
    public int FormatVersion { get; set; } = 1;

    public List<Chapter>       Chapters   { get; set; } = new();
    public List<Character>     Characters { get; set; } = new();
    public List<TimelineEvent> Timeline   { get; set; } = new();
    public List<Plotline>      Plotlines  { get; set; } = new();

    /// <summary>Absolute folder the book was loaded from / saved to. Not serialised.</summary>
    [JsonIgnore] public string FolderPath { get; set; } = string.Empty;

    public int TotalWords => Chapters.Sum(c => c.WordCount);
}

public enum ChapterStatus { Outline, Draft, Revised, Final }

/// <summary>Chapter metadata. The prose lives in <c>chapters/{Id}.xaml</c>.</summary>
public sealed class Chapter
{
    public Guid          Id             { get; set; } = Guid.NewGuid();
    public string        Title          { get; set; } = "New Chapter";
    public string        Summary        { get; set; } = string.Empty;
    public ChapterStatus Status         { get; set; } = ChapterStatus.Outline;
    public int           WordCount      { get; set; }
    public Guid?         PovCharacterId { get; set; }
    public List<Guid>    CharacterIds   { get; set; } = new();
    public List<Guid>    PlotlineIds    { get; set; } = new();
    public string        Notes          { get; set; } = string.Empty;
    public DateTime      ModifiedUtc    { get; set; } = DateTime.UtcNow;
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

/// <summary>A thread of the story. Plotlines converge; that is recorded on both sides.</summary>
public sealed class Plotline
{
    public Guid           Id           { get; set; } = Guid.NewGuid();
    public string         Name         { get; set; } = "New Plotline";
    public string         Summary      { get; set; } = string.Empty;
    public PlotlineStatus Status       { get; set; } = PlotlineStatus.Planned;
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
