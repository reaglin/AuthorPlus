using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

public class BulletsTests
{
    [Fact]
    public void Each_line_is_an_entry_and_blank_lines_are_dropped()
    {
        var bullets = Bullets.Parse("Tall, grey at the temples\n\nSpeaks slowly when he is angry\n");
        Assert.Equal(2, bullets.Count);
        Assert.Equal("Tall, grey at the temples", bullets[0].Text);
        Assert.False(bullets[1].FromAi);
    }

    [Fact]
    public void A_line_that_starts_with_the_mark_belongs_to_the_ai_and_the_mark_is_not_part_of_it()
    {
        var bullets = Bullets.Parse("Tall\n(AI) 5 ft 11 in, with dark black hair");
        Assert.False(bullets[0].FromAi);
        Assert.True(bullets[1].FromAi);
        Assert.Equal("5 ft 11 in, with dark black hair", bullets[1].Text);
    }

    [Fact]
    public void A_leading_dash_or_bullet_the_author_typed_is_not_part_of_the_entry()
    {
        var bullets = Bullets.Parse("- Tall\n• Quiet\n* Careful");
        Assert.Equal(new[] { "Tall", "Quiet", "Careful" }, bullets.Select(b => b.Text));
    }

    [Fact]
    public void Adding_marks_the_ai_entry_and_keeps_what_is_there()
    {
        var field = Bullets.Add("Tall", "5 ft 11 in", fromAi: true);
        Assert.Equal("Tall\n(AI) 5 ft 11 in", field);
        Assert.Equal(1, Bullets.AiCount(field));
    }

    [Fact]
    public void The_same_entry_is_never_added_twice_whoever_wrote_it()
    {
        var field = Bullets.Add("Tall", "tall", fromAi: true);
        Assert.Equal("Tall", field);
    }

    [Fact]
    public void An_old_single_paragraph_field_reads_as_one_entry()
    {
        var bullets = Bullets.Parse("A tall man in his fifties who has stopped expecting to be believed.");
        Assert.Single(bullets);
        Assert.False(bullets[0].FromAi);
    }

    [Fact]
    public void Labelled_replies_are_filed_by_field_and_anything_else_is_dropped()
    {
        var reply = "Here is what I found:\nPHYSICAL | 5 ft 11 in, dark black hair\nARC: learns to ask for help\nNONSENSE | ignore me\njust a sentence";
        var lines = LabeledLines.Parse(reply, new[] { "PHYSICAL", "ARC", "MOTIVATIONS" });
        Assert.Equal(2, lines.Count);
        Assert.Equal(("PHYSICAL", "5 ft 11 in, dark black hair"), lines[0]);
        Assert.Equal(("ARC", "learns to ask for help"), lines[1]);
    }
}

public class PlotlineRoleTests
{
    private static Book BookWith(int chapters)
    {
        var book = new Book { Title = "T" };
        for (int i = 0; i < chapters; i++) book.Chapters.Add(new Chapter { Title = $"Chapter {i + 1}" });
        return book;
    }

    [Fact]
    public void Seeding_makes_the_first_chapter_introduce_the_thread_and_the_rest_continue_it()
    {
        var book = BookWith(3);
        var p = new Plotline { Name = "The message" };
        p.ChapterIds.AddRange(new[] { book.Chapters[2].Id, book.Chapters[0].Id });   // out of reading order
        book.Plotlines.Add(p);

        book.SeedRoles(p);

        Assert.Equal(PlotlineRole.Introduced, p.RoleIn(book.Chapters[0].Id));
        Assert.Equal(PlotlineRole.Continuing, p.RoleIn(book.Chapters[2].Id));
        Assert.Null(p.RoleIn(book.Chapters[1].Id));
    }

    [Fact]
    public void Seeding_never_overwrites_a_part_the_author_has_already_chosen()
    {
        var book = BookWith(2);
        var p = new Plotline();
        p.ChapterIds.Add(book.Chapters[0].Id);
        book.SetRole(p, book.Chapters[0].Id, PlotlineRole.Resolved);
        p.ChapterIds.Add(book.Chapters[1].Id);

        book.SeedRoles(p);

        Assert.Equal(PlotlineRole.Resolved, p.RoleIn(book.Chapters[0].Id));
    }

    [Fact]
    public void The_status_follows_the_chapters()
    {
        var book = BookWith(2);
        var p = new Plotline();
        book.RecomputeStatus(p);
        Assert.Equal(PlotlineStatus.Planned, p.Status);

        book.SetRole(p, book.Chapters[0].Id, PlotlineRole.Introduced);
        Assert.Equal(PlotlineStatus.Active, p.Status);

        book.SetRole(p, book.Chapters[1].Id, PlotlineRole.Resolved);
        Assert.Equal(PlotlineStatus.Resolved, p.Status);

        book.SetRole(p, book.Chapters[1].Id, null);
        Assert.Equal(PlotlineStatus.Active, p.Status);
        Assert.DoesNotContain(book.Chapters[1].Id, p.ChapterIds);
    }

    [Fact]
    public void Taking_a_thread_out_of_every_chapter_makes_it_planned_again()
    {
        var book = BookWith(1);
        var p = new Plotline();
        book.SetRole(p, book.Chapters[0].Id, PlotlineRole.Continuing);
        book.SetRole(p, book.Chapters[0].Id, null);
        Assert.Equal(PlotlineStatus.Planned, p.Status);
        Assert.Empty(p.ChapterRoles);
    }

    [Fact]
    public void The_span_reads_as_a_sentence_of_chapter_numbers()
    {
        var book = BookWith(5);
        var p = new Plotline();
        book.SetRole(p, book.Chapters[0].Id, PlotlineRole.Introduced);
        book.SetRole(p, book.Chapters[2].Id, PlotlineRole.Continuing);
        book.SetRole(p, book.Chapters[4].Id, PlotlineRole.Resolved);

        var span = book.RoleSpan(p);
        Assert.Contains("Introduced ch. 1", span);
        Assert.Contains("resolved ch. 5", span);
        Assert.Contains("3 chapters", span);
    }

    [Fact]
    public void A_thread_in_no_chapter_has_no_span()
    {
        var book = BookWith(2);
        Assert.Equal("", book.RoleSpan(new Plotline()));
    }
}

public class CharacterMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AuthorPlusTests", Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void A_format_2_description_becomes_the_physical_description_and_is_not_written_again()
    {
        var store = new BookStore(_root);
        var book = store.Create("Migrating");
        var c = new Character { Name = "Roland" };
        book.Characters.Add(c);
        var p = new Plotline { Name = "The message" };
        book.Chapters.Add(new Chapter { Title = "One" });
        book.Chapters.Add(new Chapter { Title = "Two" });
        p.ChapterIds.AddRange(book.Chapters.Select(x => x.Id));
        book.Plotlines.Add(p);
        store.Save(book);

        // Rewrite the character file the way format 2 wrote it.
        var file = Directory.GetFiles(Path.Combine(book.FolderPath, "characters"), "*.json").Single();
        File.WriteAllText(file, $"{{\"Id\":\"{c.Id}\",\"Name\":\"Roland\",\"Description\":\"A tall man in his fifties.\"}}");

        var reloaded = store.Load(book.FolderPath);
        var roland = reloaded.Characters.Single();
        Assert.Equal("A tall man in his fifties.", roland.PhysicalDescription);
        Assert.Equal(Book.CurrentFormatVersion, reloaded.FormatVersion);

        // Plotline chapters got their parts on the way in.
        Assert.Equal(PlotlineRole.Introduced, reloaded.Plotlines[0].RoleIn(reloaded.Chapters[0].Id));
        Assert.Equal(PlotlineRole.Continuing, reloaded.Plotlines[0].RoleIn(reloaded.Chapters[1].Id));

        store.Save(reloaded);
        Assert.DoesNotContain("\"Description\"", File.ReadAllText(file));
    }
}

public class CharacterRoleTests
{
    private static Book BookWith(int chapters, out Character person)
    {
        var book = new Book { Title = "T" };
        for (int i = 0; i < chapters; i++) book.Chapters.Add(new Chapter { Title = $"Chapter {i + 1}" });
        person = new Character { Name = "Lucy" };
        book.Characters.Add(person);
        return book;
    }

    [Fact]
    public void The_first_chapter_that_lists_them_introduces_them_and_the_rest_are_appearances()
    {
        var book = BookWith(3, out var lucy);
        book.Chapters[2].CharacterIds.Add(lucy.Id);
        book.Chapters[0].CharacterIds.Add(lucy.Id);

        book.SeedRoles(lucy);

        Assert.Equal(CharacterRole.Introduced, lucy.RoleIn(book.Chapters[0].Id));
        Assert.Equal(CharacterRole.Appears, lucy.RoleIn(book.Chapters[2].Id));
        Assert.Null(lucy.RoleIn(book.Chapters[1].Id));
    }

    [Fact]
    public void Setting_a_part_puts_them_in_the_chapter_and_clearing_it_takes_them_out()
    {
        var book = BookWith(2, out var lucy);
        book.SetRole(lucy, book.Chapters[0].Id, CharacterRole.Introduced);
        Assert.Contains(lucy.Id, book.Chapters[0].CharacterIds);

        book.SetRole(lucy, book.Chapters[0].Id, null);
        Assert.DoesNotContain(lucy.Id, book.Chapters[0].CharacterIds);
        Assert.Empty(lucy.ChapterRoles);
    }

    [Fact]
    public void Taking_them_out_of_a_chapter_they_narrate_also_drops_them_as_its_point_of_view()
    {
        var book = BookWith(1, out var lucy);
        book.SetRole(lucy, book.Chapters[0].Id, CharacterRole.Appears);
        book.Chapters[0].PovCharacterId = lucy.Id;

        book.SetRole(lucy, book.Chapters[0].Id, null);

        Assert.Null(book.Chapters[0].PovCharacterId);
    }

    [Fact]
    public void Seeding_never_overwrites_a_part_the_author_chose()
    {
        var book = BookWith(2, out var lucy);
        book.SetRole(lucy, book.Chapters[0].Id, CharacterRole.Leaves);
        book.Chapters[1].CharacterIds.Add(lucy.Id);

        book.SeedRoles(lucy);

        Assert.Equal(CharacterRole.Leaves, lucy.RoleIn(book.Chapters[0].Id));
        Assert.Equal(CharacterRole.Appears, lucy.RoleIn(book.Chapters[1].Id));
    }

    [Fact]
    public void The_span_names_where_they_arrive_and_where_they_go()
    {
        var book = BookWith(4, out var lucy);
        book.SetRole(lucy, book.Chapters[0].Id, CharacterRole.Introduced);
        book.SetRole(lucy, book.Chapters[1].Id, CharacterRole.Appears);
        book.SetRole(lucy, book.Chapters[3].Id, CharacterRole.Leaves);
        book.Chapters[1].PovCharacterId = lucy.Id;

        var span = book.RoleSpan(lucy);

        Assert.Contains("Introduced ch. 1", span);
        Assert.Contains("leaves ch. 4", span);
        Assert.Contains("1 told from their point of view", span);
    }

    [Fact]
    public void A_character_in_no_chapter_has_no_span()
    {
        var book = BookWith(2, out var lucy);
        Assert.Equal("", book.RoleSpan(lucy));
    }
}
