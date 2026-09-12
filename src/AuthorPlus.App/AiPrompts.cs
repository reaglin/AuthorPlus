using Eaglin.AiManager.Prompts;

namespace AuthorPlus.App;

/// <summary>
/// AuthorPlus's built-in prompt templates. Registered with the AI Manager at startup; the user
/// can edit the wording in AI › Prompt Library, and an edited template is never overwritten
/// by a newer default here. Placeholders are filled by the code that runs each template.
/// </summary>
internal static class AiPrompts
{
    public const string ChapterSummary   = "chapter-summary";
    public const string CharacterProfile = "character-profile";

    public static IReadOnlyList<PromptTemplate> Defaults { get; } = new[]
    {
        new PromptTemplate(ChapterSummary,
            system: "You are an editorial assistant for a novelist. Summarize the chapter in 3–5 sentences of plain prose: " +
                    "what happens, who is involved, and what changes. Do not praise, critique, or add anything not in the text.",
            user:   "Book: {book}\nChapter: {chapter}\n\n{text}",
            description: "Three to five sentences saying what happens in a chapter. Fills the chapter's Summary field.",
            maxTokens: 1000),

        new PromptTemplate(CharacterProfile,
            system: "You are a story-development assistant. Given what the author already knows about a character and the chapter " +
                    "summaries, suggest additions for the blank or thin fields. Return plain text with headings exactly: " +
                    "Description, Motivations, Actions, Arc. Keep each under 120 words. Never contradict what is given.",
            user:   "Book: {book}\nSynopsis: {synopsis}\n\nCharacter as known:\n{known}\n\nChapter summaries:\n{summaries}",
            description: "Suggestions for a character's Description, Motivations, Actions and Arc, appended to the character's Notes.",
            maxTokens: 1500)
    };
}
