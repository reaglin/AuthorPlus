using Eaglin.AiManager.Prompts;

namespace AuthorPlus.App;

/// <summary>
/// AuthorPlus's built-in prompt templates. Registered with the AI Manager at startup; the user
/// can edit the wording in AI › Prompt Library, and an edited template is never overwritten
/// by a newer default here. Placeholders are filled by the code that runs each template.
/// </summary>
internal static class AiPrompts
{
    public const string ChapterSummary      = "chapter-summary";
    public const string ChapterAnalysis     = "chapter-analysis";
    public const string CharactersInChapter = "characters-in-chapter";
    public const string CharacterProfile    = "character-profile";
    public const string SectionOutline      = "section-outline";
    public const string SectionSummary      = "section-summary";

    public static IReadOnlyList<PromptTemplate> Defaults { get; } = new[]
    {
        new PromptTemplate(ChapterSummary,
            system: "You are an editorial assistant for a novelist. Summarize the chapter in 3–5 sentences of plain prose: " +
                    "what happens, who is involved, and what changes. Do not praise, critique, or add anything not in the text.",
            user:   "Book: {book}\nChapter: {chapter}\n\n{text}",
            description: "Three to five sentences saying what happens in a chapter. Becomes the chapter's Summary item.",
            maxTokens: 1000),

        new PromptTemplate(ChapterAnalysis,
            system: "You are an experienced fiction editor giving a working author frank, specific, useful notes. " +
                    "Assess the chapter on: pacing, tension and stakes; point of view and voice; dialogue; clarity and continuity " +
                    "with what came before; prose habits worth fixing. Quote short phrases from the text when you point at something. " +
                    "End with the three changes that would help most. Use plain headings and short paragraphs; no praise padding.",
            user:   "Book: {book}\nSection: {section}\nChapter: {chapter}\n\nWhat has happened so far (summaries of earlier chapters):\n{context}\n\nThe chapter:\n{text}",
            description: "An editorial critique of one chapter, with the earlier chapters' summaries as context. Kept as a dated Analysis item; run it on several providers to compare.",
            maxTokens: 3000),

        new PromptTemplate(CharactersInChapter,
            system: "You are helping an author track which characters appear in a chapter. Read the chapter and list every named " +
                    "character who appears or acts in it (not merely mentioned in passing). Output one character per line as " +
                    "\"Name — what they do in this chapter\" (one short clause). Put the point-of-view character first and mark that line " +
                    "with \"(POV)\". Use the author's known character names where they match; otherwise use the name as written.",
            user:   "Chapter: {chapter}\n\nKnown characters (name — role):\n{known_characters}\n\n{text}",
            description: "Which characters appear in a chapter, with what each does; seeds the Characters-in-chapter item.",
            maxTokens: 800),

        new PromptTemplate(CharacterProfile,
            system: "You are a story-development assistant. Given what the author already knows about a character and the chapter " +
                    "summaries, suggest additions for the blank or thin fields. Return plain text with headings exactly: " +
                    "Description, Motivations, Actions, Arc. Keep each under 120 words. Never contradict what is given.",
            user:   "Book: {book}\nSynopsis: {synopsis}\n\nCharacter as known:\n{known}\n\nChapter summaries:\n{summaries}",
            description: "Suggestions for a character's Description, Motivations, Actions and Arc, appended to the character's Notes.",
            maxTokens: 1500),

        new PromptTemplate(SectionOutline,
            system: "You are an editorial assistant. From the chapter summaries, write a numbered chapter-flow outline: one line per " +
                    "chapter, numbered in order, each a single sentence naming the key event and who drives it. No commentary.",
            user:   "Book: {book}\nSection: {section}\n\nChapter summaries in order:\n{summaries}",
            description: "A numbered one-line-per-chapter outline of a section, in the style of the author's own chapter-flow notes. Becomes an Outline item on the section.",
            maxTokens: 1500),

        new PromptTemplate(SectionSummary,
            system: "You are an editorial assistant. Summarize this part of the book in one paragraph of 5–8 sentences: the situation " +
                    "at the start, the main turns, and where things stand at the end. Plain prose, no headings, nothing not in the summaries.",
            user:   "Book: {book}\nSection: {section}\n\nChapter summaries in order:\n{summaries}",
            description: "One paragraph summarizing a whole section from its chapter summaries. Fills the section's Summary field.",
            maxTokens: 800)
    };
}
