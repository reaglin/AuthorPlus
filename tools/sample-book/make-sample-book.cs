#:project ../../src/AuthorPlus.Core/AuthorPlus.Core.csproj
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Writes "The Keeper of Gull Point", a short original sample book used for the Store screenshots
// (tools/store-shots.ps1). Usage: dotnet run tools/sample-book/make-sample-book.cs -- <library folder>
// Prints the book folder. The text is written for this sample; nothing in it is anyone's manuscript.
using System.Security;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

var root = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "authorplus-sample");
var store = new BookStore(root);
var book = store.Create("The Keeper of Gull Point", "A. Sample");
book.Genre = "Literary mystery";
book.Synopsis = "When the lamp at Gull Point goes dark on the night a fishing boat is lost, the keeper's daughter sets out to learn who put it out — and why her father lied about it.";
book.TargetWords = 80000;

var part1 = new Section { Title = "Part One — The Dark Night", Summary = "The lamp fails, a boat is lost, and Nell begins to doubt her father." };
var part2 = new Section { Title = "Part Two — The Ledger", Summary = "Nell follows the keeper's log to the harbourmaster and the truth about the wreck." };
book.Sections.AddRange(new[] { part1, part2 });

var nell   = new Character { Name = "Nell Carrow", Role = "Protagonist", Aliases = "Nell\nthe keeper's girl",
    PhysicalDescription = "Seventeen, tall for her age\nSalt-cracked hands from the lamp room",
    Personality = "Stubborn and exact\nKeeps her fear to herself",
    Motivations = "To learn why the lamp went dark\nTo believe her father again",
    Actions = "Finds the cold wick in chapter 1\nSteals the keeper's log in chapter 4",
    Arc = "From obedient daughter to the one who keeps the light" };
var tom    = new Character { Name = "Tom Carrow", Role = "Keeper of the light", Aliases = "the keeper\nFather",
    PhysicalDescription = "Grey beard, a limp from an old fall",
    Personality = "Kind, proud, frightened of losing the post",
    Motivations = "To keep his post and his secret", Arc = "Confesses in chapter 6" };
var ames   = new Character { Name = "Silas Ames", Role = "Antagonist", Aliases = "the harbourmaster",
    PhysicalDescription = "Neat coat, ink on his cuffs", Personality = "Patient, plausible, cold",
    Motivations = "To hide the insurance on the lost boat" };
var ruth   = new Character { Name = "Ruth Penhale", Role = "Supporting", Aliases = "Ruth",
    Personality = "Widow of the lost boat's skipper; sharp-tongued, grieving", Motivations = "To have someone answer for it" };
book.Characters.AddRange(new[] { nell, tom, ames, ruth });

string[][] chapters =
{
    new[] { "The Cold Wick", "1",
        "The lamp at Gull Point had burned every night for forty-one years, and on the forty-second it went out.",
        "Nell found it at a quarter past two, when the wind dropped and the quiet woke her. She climbed the hundred and nine steps with her hand on the cold rail, counting the way her father had taught her, and at the top the lamp room was black. The wick was not burnt down. It had been trimmed, neatly, the way only a keeper trims it, and then pinched out.",
        "* * *",
        "By morning the boats were in, all but one. Ruth Penhale stood on the slip with her shawl over her mouth and did not cry, and that was worse.",
        "\"You were up there,\" her father said when Nell came down. It was not a question. \"You saw it burning.\"",
        "She looked at his hands. There was soot on the thumb and forefinger of the right one, and he saw her see it." },
    new[] { "Ruth on the Slip", "1",
        "Ruth would not come into the cottage. She stood at the gate with the wind pulling at her skirts and asked for the keeper by his name, not his title, which was how Nell knew she had come to accuse him.",
        "\"The Mary Ann was two miles out,\" Ruth said. \"Jack would have seen your light. He always said he could steer home on it blind.\"",
        "Her father said the lamp had burned all night. Nell said nothing at all, and the saying nothing sat in her like a stone." },
    new[] { "Ink on His Cuffs", "1",
        "The harbourmaster came at noon, with a ledger under his arm and his coat buttoned to the throat. Silas Ames was the kind of man who wrote everything down, and Nell had always thought that a comfort.",
        "He asked her father for the log. He read the night's entry twice, nodded, and closed it. \"Burned till dawn,\" he said. \"That settles it.\"",
        "As he turned to go, Nell saw the fresh ink on his cuff, and the page of the log that was not quite flat." },
    new[] { "The Keeper's Log", "2",
        "She took the log on the fourth night, while her father slept in his chair with the Bible open on his knee.",
        "The entry for the dark night was in his hand, but the page before it had been cut out with a blade, close to the spine, so cleanly that only a keeper's daughter, who had ruled those lines herself, would ever have known." },
    new[] { "The Insurance", "2",
        "The clerk at the shipping office was bored, and boredom made him generous. The Mary Ann, he said, had been insured the week before she sailed — for twice what she was worth, by a buyer who had not yet paid for her.",
        "\"Who?\" Nell asked, though she knew." },
    new[] { "What the Light Saw", "2",
        "Her father told her on the gallery, with the lamp turning behind them and the sea going white and black and white.",
        "Ames had come up the tower that night. Ames had a paper that said the post was his to give and his to take away. Ames had pinched the wick himself, and her father had let him, and then had written in the log that it burned till dawn.",
        "\"I kept the light for forty-one years,\" he said. \"I could not keep it one more night.\"",
        "Nell took the matches from his pocket. \"Then I will.\"" }
};

var bodies = new List<(Chapter, string, int)>();
for (int i = 0; i < chapters.Length; i++)
{
    var c = chapters[i];
    var ch = new Chapter { Title = c[0], SectionId = c[1] == "1" ? part1.Id : part2.Id,
        Status = i < 3 ? ChapterStatus.Revised : ChapterStatus.Draft, TargetWords = 3000, PovCharacterId = nell.Id };
    book.Chapters.Add(ch);
    var paras = string.Concat(c.Skip(2).Select(p => p == "* * *"
        ? "<Paragraph TextAlignment=\"Center\">* * *</Paragraph>"
        : $"<Paragraph>{SecurityElement.Escape(p)}</Paragraph>"));
    bodies.Add((ch, $"<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" FontFamily=\"Georgia\" FontSize=\"16\" PagePadding=\"40\">{paras}</FlowDocument>", WordCounter.Count(string.Join(" ", c.Skip(2)))));
}
var ch1 = book.Chapters[0];

void In(Character who, params int[] idx)
{
    foreach (var i in idx) book.Chapters[i].CharacterIds.Add(who.Id);
    book.SeedRoles(who);
}
In(nell, 0, 1, 2, 3, 4, 5); In(tom, 0, 1, 2, 5); In(ruth, 0, 1); In(ames, 2, 4, 5);
book.SetRole(ruth, book.Chapters[1].Id, CharacterRole.Leaves);

Plotline Thread(string name, PlotlineKind kind, string summary, int[] idx, int? resolved)
{
    var p = new Plotline { Name = name, Kind = kind, Summary = summary };
    foreach (var i in idx) { p.ChapterIds.Add(book.Chapters[i].Id); book.Chapters[i].PlotlineIds.Add(p.Id); }
    book.Plotlines.Add(p);
    book.SeedRoles(p);
    if (resolved is int r) book.SetRole(p, book.Chapters[r].Id, PlotlineRole.Resolved);
    return p;
}
var dark  = Thread("Who put out the lamp", PlotlineKind.Primary, "The lamp is pinched out on the night the Mary Ann is lost\nNell's search for who did it drives the book", new[] { 0, 1, 2, 3, 5 }, 5);
var lie   = Thread("Father's lie", PlotlineKind.Secondary, "Tom swears the lamp burned till dawn\nNell knows it did not", new[] { 0, 1, 3, 5 }, 5);
var money = Thread("The insured boat", PlotlineKind.Subplot, "The Mary Ann was over-insured a week before she sailed", new[] { 2, 4, 5 }, null);
dark.CharacterIds.AddRange(new[] { nell.Id, tom.Id, ames.Id }); lie.CharacterIds.AddRange(new[] { nell.Id, tom.Id }); money.CharacterIds.Add(ames.Id);

book.Timeline.AddRange(new[]
{
    new TimelineEvent { Order = 1, When = "A week before", Title = "The Mary Ann is insured", ChapterIds = { book.Chapters[4].Id }, CharacterIds = { ames.Id } },
    new TimelineEvent { Order = 2, When = "The dark night, 1 a.m.", Title = "Ames climbs the tower", ChapterIds = { book.Chapters[5].Id }, CharacterIds = { ames.Id, tom.Id } },
    new TimelineEvent { Order = 3, When = "The dark night, 2:15 a.m.", Title = "Nell finds the cold wick", ChapterIds = { ch1.Id }, CharacterIds = { nell.Id } },
    new TimelineEvent { Order = 4, When = "The next noon", Title = "Ames reads the log", ChapterIds = { book.Chapters[2].Id }, CharacterIds = { ames.Id, tom.Id, nell.Id } },
});

book.Items.Add(new Item { OwnerId = ch1.Id, Kind = ItemKind.Summary, Title = "Summary", Provider = "Claude", Model = "claude-sonnet-5-5", PromptName = "chapter-summary",
    Body = "What happens\n- The lamp at Gull Point goes out for the first time in forty-one years\n- Nell climbs the tower and finds the wick trimmed and pinched out by hand\n- The Mary Ann does not come home; Ruth Penhale waits on the slip\n- Nell's father insists she saw the lamp burning\n\nCharacters\n- Nell Carrow (introduced) — finds the cold wick\n- Tom Carrow (introduced) — lies about the night; soot on his fingers\n- Ruth Penhale (introduced) — the lost skipper's wife" });
book.Items.Add(new Item { OwnerId = ch1.Id, Kind = ItemKind.Analysis, Title = "Analysis", Provider = "Claude", Model = "claude-sonnet-5-5", PromptName = "chapter-analysis",
    Body = "## Pacing\n1. The opening line lands the inciting event at once; the climb slows it just enough to build dread.\n2. The scene break before the slip is well placed — the reader fills the night themselves.\n\n## Tension\n1. The soot on Tom's fingers is the chapter's sharpest moment; consider ending the chapter on his look rather than on her seeing it.\n\n## Stakes\n1. A boat is lost and a father may be lying — both stakes are clear by the last line.\n\n## Point of view and voice\n1. Close third on Nell holds throughout; the counted steps are a good, specific detail.\n\n## Dialogue\n1. Tom's \"It was not a question\" line tells what the dialogue already shows; it can go.\n\n## Continuity\n1. The lamp has burned \"forty-one years\" — keep this consistent with Tom's age in chapter 6.\n\n## Prose habits\n1. Several sentences open with \"She\"; vary two of them.\n\n## Three changes\n1. End on Tom's look.\n2. Cut the line that explains his question.\n3. Vary the sentence openings in the climb." });

store.Save(book);
foreach (var (ch, xaml, words) in bodies)
    store.SaveChapterBody(book, ch, xaml, words);
store.Save(book);                          // the chapters' word counts, now that their text is written
Console.WriteLine(book.FolderPath);
