using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

public enum Severity { Info, Warning, Error }

/// <summary>One thing the checker noticed. <see cref="TargetId"/> is the node to show the author.</summary>
public sealed record Finding(Severity Severity, string Rule, string Message, Guid? TargetId)
{
    public override string ToString() => $"{Severity}: {Message}";
}

/// <summary>
/// Rule-based checks over the book's own records — no AI, instant, deterministic. Each rule is
/// a small method so a new rule is one method and one test. The text of chapters is read only
/// through <paramref name="textOf"/>, so callers decide whether to pay for loading it.
/// </summary>
public static class Consistency
{
    public static IReadOnlyList<Finding> Check(Book book, Func<Chapter, string>? textOf = null)
    {
        var f = new List<Finding>();
        DanglingReferences(book, f);
        PovNotPresent(book, f);
        PlotlinesUnresolved(book, f);
        PlotlinesWithoutChapters(book, f);
        ConvergencesUnpaired(book, f);
        EventsOutOfChapterOrder(book, f);
        ChaptersWithoutSummary(book, f);
        if (textOf != null) MentionedBeforeIntroduced(book, textOf, f);
        return f.OrderByDescending(x => x.Severity).ThenBy(x => x.Rule).ToList();
    }

    // ── Rules ─────────────────────────────────────────────────────────────────

    /// <summary>Ids that point at chapters, characters or plotlines that no longer exist.</summary>
    internal static void DanglingReferences(Book b, List<Finding> f)
    {
        var chapters   = b.Chapters.Select(c => c.Id).ToHashSet();
        var characters = b.Characters.Select(c => c.Id).ToHashSet();
        var plotlines  = b.Plotlines.Select(p => p.Id).ToHashSet();

        foreach (var ch in b.Chapters)
        {
            if (ch.PovCharacterId is { } pov && !characters.Contains(pov))
                f.Add(new(Severity.Error, "dangling", $"Chapter \"{ch.Title}\" names a point-of-view character that no longer exists.", ch.Id));
            if (ch.CharacterIds.Any(id => !characters.Contains(id)))
                f.Add(new(Severity.Error, "dangling", $"Chapter \"{ch.Title}\" links a character that no longer exists.", ch.Id));
            if (ch.PlotlineIds.Any(id => !plotlines.Contains(id)))
                f.Add(new(Severity.Error, "dangling", $"Chapter \"{ch.Title}\" links a plotline that no longer exists.", ch.Id));
        }
        foreach (var ev in b.Timeline)
        {
            if (ev.ChapterIds.Any(id => !chapters.Contains(id)))
                f.Add(new(Severity.Error, "dangling", $"Event \"{ev.Title}\" refers to a chapter that no longer exists.", ev.Id));
            if (ev.CharacterIds.Any(id => !characters.Contains(id)))
                f.Add(new(Severity.Error, "dangling", $"Event \"{ev.Title}\" refers to a character that no longer exists.", ev.Id));
            if (ev.PlotlineIds.Any(id => !plotlines.Contains(id)))
                f.Add(new(Severity.Error, "dangling", $"Event \"{ev.Title}\" refers to a plotline that no longer exists.", ev.Id));
        }
        foreach (var p in b.Plotlines)
        {
            if (p.ChapterIds.Any(id => !chapters.Contains(id)))
                f.Add(new(Severity.Error, "dangling", $"Plotline \"{p.Name}\" runs through a chapter that no longer exists.", p.Id));
            if (p.CharacterIds.Any(id => !characters.Contains(id)))
                f.Add(new(Severity.Error, "dangling", $"Plotline \"{p.Name}\" involves a character that no longer exists.", p.Id));
            if (p.Convergences.Any(c => !plotlines.Contains(c.OtherPlotlineId) || (c.ChapterId is { } cid && !chapters.Contains(cid))))
                f.Add(new(Severity.Error, "dangling", $"Plotline \"{p.Name}\" converges with a plotline or in a chapter that no longer exists.", p.Id));
        }
    }

    /// <summary>The POV character should be among the chapter's characters.</summary>
    internal static void PovNotPresent(Book b, List<Finding> f)
    {
        foreach (var ch in b.Chapters)
            if (ch.PovCharacterId is { } pov && b.Characters.Any(c => c.Id == pov) && !ch.CharacterIds.Contains(pov))
                f.Add(new(Severity.Warning, "pov", $"Chapter \"{ch.Title}\": the point-of-view character is not listed among the characters present.", ch.Id));
    }

    /// <summary>A plotline that is Active or Planned at the end of the book was never resolved.</summary>
    internal static void PlotlinesUnresolved(Book b, List<Finding> f)
    {
        foreach (var p in b.Plotlines.Where(p => p.Status != PlotlineStatus.Resolved))
            f.Add(new(p.Status == PlotlineStatus.Active ? Severity.Warning : Severity.Info, "unresolved",
                $"Plotline \"{p.Name}\" is {p.Status.ToString().ToLowerInvariant()} — it never reaches Resolved.", p.Id));
    }

    /// <summary>An Active or Resolved plotline that runs through no chapter.</summary>
    internal static void PlotlinesWithoutChapters(Book b, List<Finding> f)
    {
        foreach (var p in b.Plotlines.Where(p => p.Status != PlotlineStatus.Planned && p.ChapterIds.Count == 0))
            f.Add(new(Severity.Warning, "orphan-plotline", $"Plotline \"{p.Name}\" is {p.Status.ToString().ToLowerInvariant()} but runs through no chapter.", p.Id));
    }

    /// <summary>A convergence is recorded on one side only.</summary>
    internal static void ConvergencesUnpaired(Book b, List<Finding> f)
    {
        foreach (var p in b.Plotlines)
            foreach (var c in p.Convergences)
            {
                var other = b.Plotlines.FirstOrDefault(o => o.Id == c.OtherPlotlineId);
                if (other == null) continue;
                if (!other.Convergences.Any(x => x.OtherPlotlineId == p.Id))
                    f.Add(new(Severity.Info, "convergence", $"Plotline \"{p.Name}\" converges with \"{other.Name}\", but \"{other.Name}\" does not record it.", other.Id));
            }
    }

    /// <summary>
    /// Story order vs chapter order: an event told in a later chapter than an event that follows it
    /// in the story. Legitimate (flashbacks) — reported as information so the author can confirm.
    /// </summary>
    internal static void EventsOutOfChapterOrder(Book b, List<Finding> f)
    {
        var chapterIndex = b.Chapters.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var placed = b.Timeline.OrderBy(e => e.Order)
            .Select(e => (e, first: e.ChapterIds.Where(chapterIndex.ContainsKey).Select(id => chapterIndex[id]).DefaultIfEmpty(-1).Min()))
            .Where(x => x.first >= 0).ToList();
        for (int i = 1; i < placed.Count; i++)
        {
            var (prev, prevCh) = placed[i - 1];
            var (cur, curCh) = placed[i];
            if (curCh < prevCh)
                f.Add(new(Severity.Info, "story-order",
                    $"\"{cur.Title}\" comes after \"{prev.Title}\" in the story but is told earlier (chapter {curCh + 1} vs {prevCh + 1}).", cur.Id));
        }
    }

    internal static void ChaptersWithoutSummary(Book b, List<Finding> f)
    {
        var n = b.Chapters.Count(c => b.SummaryText(c).Length == 0);
        if (n > 0 && b.Chapters.Count > 0)
            f.Add(new(Severity.Info, "summary", $"{n} of {b.Chapters.Count} chapters have no summary yet (AI › Summarize).", null));
    }

    /// <summary>
    /// A character named in a chapter's text earlier than the first chapter that lists them as
    /// present. Usually means the link is missing, sometimes a real continuity slip.
    /// </summary>
    internal static void MentionedBeforeIntroduced(Book b, Func<Chapter, string> textOf, List<Finding> f)
    {
        if (b.Characters.Count == 0) return;
        var firstLinked = new Dictionary<Guid, int>();
        for (int i = 0; i < b.Chapters.Count; i++)
            foreach (var id in b.Chapters[i].CharacterIds)
                firstLinked.TryAdd(id, i);

        for (int i = 0; i < b.Chapters.Count; i++)
        {
            var ch = b.Chapters[i];
            foreach (var m in MentionFinder.Find(textOf(ch), b.Characters))
            {
                var c = b.Characters.First(x => x.Id == m.CharacterId);
                if (!firstLinked.TryGetValue(c.Id, out var intro))
                    f.Add(new(Severity.Info, "unlinked", $"\"{c.Name}\" is named {m.Count}× in chapter \"{ch.Title}\" but is not linked to any chapter (Character › Scan chapters for mentions).", ch.Id));
                else if (i < intro && !ch.CharacterIds.Contains(c.Id))
                    f.Add(new(Severity.Warning, "before-intro", $"\"{c.Name}\" is named in chapter \"{ch.Title}\" before the first chapter that lists them present (\"{b.Chapters[intro].Title}\").", ch.Id));
            }
        }
    }
}
