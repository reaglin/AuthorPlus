﻿using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

public class SectionsAndItemsTests
{
    [Fact]
    public void Sections_chapters_and_items_round_trip_in_order_with_one_file_each()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("Trilogy");

        var part1 = new Section { Title = "Part 1" };
        var part2 = new Section { Title = "Part 2" };
        book.Sections.AddRange(new[] { part1, part2 });
        var c1 = new Chapter { Title = "One", SectionId = part1.Id };
        var c2 = new Chapter { Title = "Two", SectionId = part1.Id };
        var c3 = new Chapter { Title = "Three", SectionId = part2.Id };
        var loose = new Chapter { Title = "Loose" };
        book.Chapters.AddRange(new[] { c1, c2, c3, loose });

        book.SetSummary(c1, "Roland finds the message.", "Claude", "claude-opus-5", "chapter-summary");
        book.Items.Add(new Item { OwnerId = c1.Id, Kind = ItemKind.Analysis, Title = "Analysis 1", Body = "Strong opening.", Provider = "Gemini" });
        book.Items.Add(new Item { OwnerId = part2.Id, Kind = ItemKind.Outline, Title = "Outline", Body = "1. ..." });
        book.Items.Add(new Item { OwnerId = book.Id, Kind = ItemKind.Notes, Title = "Book notes", Body = "Trilogy." });
        store.Save(book);

        Assert.Equal(2, Directory.GetFiles(Path.Combine(book.FolderPath, "sections"), "*.json").Length);
        Assert.Equal(4, Directory.GetFiles(Path.Combine(book.FolderPath, "items"), "*.json").Length);

        var loaded = store.Load(book.FolderPath);
        Assert.Equal(Book.CurrentFormatVersion, loaded.FormatVersion);
        Assert.Equal(new[] { "Part 1", "Part 2" }, loaded.Sections.Select(s => s.Title));
        Assert.Equal(new[] { "One", "Two" }, loaded.ChaptersOf(loaded.Sections[0]).Select(c => c.Title));
        Assert.Equal(new[] { "Three" }, loaded.ChaptersOf(loaded.Sections[1]).Select(c => c.Title));
        Assert.Equal(new[] { "Loose" }, loaded.UnsectionedChapters.Select(c => c.Title));
        Assert.Equal(2, loaded.ChapterNumber(loaded.Chapters[1]));
        Assert.Equal(1, loaded.ChapterNumber(loaded.Chapters[2]));
        Assert.Equal(1, loaded.ChapterNumber(loaded.Chapters[3]));

        var summary = loaded.SummaryOf(loaded.Chapters[0]);
        Assert.NotNull(summary);
        Assert.Equal("Roland finds the message.", summary!.Body);
        Assert.Equal("claude-opus-5", summary.Model);
        Assert.True(summary.IsAiMade);
        Assert.Equal(new[] { ItemKind.Summary, ItemKind.Analysis }, loaded.ItemsOf(loaded.Chapters[0].Id).Select(i => i.Kind));
        Assert.Equal(ItemKind.Outline, loaded.ItemsOf(loaded.Sections[1].Id).Single().Kind);
        Assert.Equal("Book notes", loaded.ItemsOf(loaded.Id).Single().Title);

        var raw = File.ReadAllText(Path.Combine(book.FolderPath, "items", summary.Id.ToString("N") + ".json"));
        Assert.Contains("\"Kind\": \"Summary\"", raw);
    }

    [Fact]
    public void SetSummary_keeps_one_summary_per_chapter()
    {
        var book = new Book();
        var ch = new Chapter();
        book.Chapters.Add(ch);
        book.SetSummary(ch, "first");
        book.SetSummary(ch, "second");
        Assert.Single(book.Items);
        Assert.Equal("second", book.SummaryText(ch));
        Assert.Equal(string.Empty, book.SummaryText(new Chapter()));
    }

    [Fact]
    public void Removing_a_chapter_or_section_removes_its_items_and_orphans_are_pruned_on_save()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("Prune");
        var part = new Section { Title = "Part" };
        book.Sections.Add(part);
        var ch = new Chapter { Title = "Ch", SectionId = part.Id };
        book.Chapters.Add(ch);
        book.SetSummary(ch, "s");
        book.Items.Add(new Item { OwnerId = part.Id, Kind = ItemKind.Notes });
        book.Items.Add(new Item { OwnerId = Guid.NewGuid(), Kind = ItemKind.Notes, Title = "orphan" });
        store.Save(book);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(book.FolderPath, "items"), "*.json").Length);   // orphan never written

        book.RemoveSection(part, deleteChapters: false);
        Assert.Null(book.Chapters.Single().SectionId);
        Assert.Single(book.Items);                                   // the chapter's summary survives
        book.RemoveChapter(book.Chapters.Single());
        Assert.Empty(book.Items);
        store.Save(book);
        Assert.Empty(Directory.GetFiles(Path.Combine(book.FolderPath, "items"), "*.json"));
        Assert.Empty(Directory.GetFiles(Path.Combine(book.FolderPath, "sections"), "*.json"));
    }

    [Fact]
    public void RemoveSection_can_take_its_chapters_with_it()
    {
        var book = new Book();
        var part = new Section();
        book.Sections.Add(part);
        book.Chapters.Add(new Chapter { SectionId = part.Id });
        book.Chapters.Add(new Chapter());
        book.RemoveSection(part, deleteChapters: true);
        Assert.Single(book.Chapters);
        Assert.Empty(book.Sections);
    }

    [Fact]
    public void A_format_1_book_loads_with_its_chapter_summaries_turned_into_items_and_saves_as_format_2()
    {
        using var t = new TempDir();
        var folder = Path.Combine(t.Path, "Old Book");
        Directory.CreateDirectory(Path.Combine(folder, "chapters"));
        var chId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(folder, "book.json"),
            "{ \"Id\": \"" + Guid.NewGuid() + "\", \"Title\": \"Old\", \"FormatVersion\": 1, \"ChapterOrder\": [ \"" + chId + "\" ] }");
        File.WriteAllText(Path.Combine(folder, "chapters", chId.ToString("N") + ".json"),
            "{ \"Id\": \"" + chId + "\", \"Title\": \"Ch 1\", \"Summary\": \"Old-style summary.\", \"Status\": \"Draft\", \"WordCount\": 12 }");

        var store = new BookStore(t.Path);
        var book = store.Load(folder);

        Assert.Equal(Book.CurrentFormatVersion, book.FormatVersion);
        Assert.Empty(book.Sections);
        var ch = book.Chapters.Single();
        Assert.Equal("Old-style summary.", book.SummaryText(ch));
        Assert.Null(ch.LegacySummary);
        Assert.Equal(12, ch.WordCount);

        store.Save(book);
        var chapterJson = File.ReadAllText(Path.Combine(folder, "chapters", chId.ToString("N") + ".json"));
        Assert.DoesNotContain("\"Summary\"", chapterJson);
        Assert.Contains($"\"FormatVersion\": {Book.CurrentFormatVersion}", File.ReadAllText(Path.Combine(folder, "book.json")));
        Assert.Equal("Old-style summary.", store.Load(folder).SummaryText(store.Load(folder).Chapters.Single()));
    }
}
