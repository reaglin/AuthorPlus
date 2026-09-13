using Eaglin.AiManager.Prompts;

namespace AuthorPlus.App;

/// <summary>
/// AuthorPlus's built-in prompt templates. Registered with the AI Manager at startup; the user
/// can edit the wording in AI › Prompt Library, and an edited template is never overwritten
/// by a newer default here. Placeholders are filled by the code that runs each template.
/// </summary>
public static class AiPrompts
{
    public const string ChapterSummary      = "chapter-summary";
    public const string ChunkSummary        = "chunk-summary";
    public const string MergeSummaries      = "merge-summaries";
    public const string ChapterAnalysis     = "chapter-analysis";
    public const string CharactersInChapter = "characters-in-chapter";
    public const string CharacterProfile    = "character-profile";
    public const string SectionOutline      = "section-outline";
    public const string SectionSummary      = "section-summary";
    public const string ContinuityCheck     = "continuity-check";
    public const string PlotAnalysis        = "plot-analysis";
    public const string StyleRead           = "style-read";

    /// <summary>
    /// Practical ceiling for one request's prose, in characters (≈ 100k tokens). Above it a
    /// chapter is summarised in parts (<see cref="ChunkSummary"/> then <see cref="MergeSummaries"/>)
    /// and a book-level prompt is refused with an explanation — never cut off silently.
    /// </summary>
    public const int MaxPromptChars = 400_000;

    public static IReadOnlyList<PromptTemplate> Defaults { get; } = new[]
    {
        new PromptTemplate(ChapterSummary,
            system: "You are an editorial assistant for a novelist. Summarize the chapter in 3–5 sentences of plain prose: " +
                    "what happens, who is involved, and what changes. Do not praise, critique, or add anything not in the text.",
            user:   "Book: {book}\nChapter: {chapter}\n\n{text}",
            description: "Three to five sentences saying what happens in a chapter. Becomes the chapter's Summary item.",
            maxTokens: 1000),

        new PromptTemplate(ChunkSummary,
            system: "You are an editorial assistant. This is part {part} of {parts} of one chapter. Summarize what happens in this part " +
                    "in 3–6 sentences of plain prose, naming who is involved. No commentary; nothing not in the text.",
            user:   "Book: {book}\nChapter: {chapter} (part {part} of {parts})\n\n{text}",
            description: "Used only when a chapter is too long for one request: summarizes one part of it.",
            maxTokens: 800),

        new PromptTemplate(MergeSummaries,
            system: "You are an editorial assistant. Below are summaries of consecutive parts of one chapter. Combine them into one " +
                    "summary of 3–5 sentences of plain prose: what happens, who is involved, what changes. Nothing not in the parts.",
            user:   "Book: {book}\nChapter: {chapter}\n\nPart summaries in order:\n{summaries}",
            description: "Used only when a chapter is too long for one request: merges the part summaries into the chapter summary.",
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
            maxTokens: 800),

        new PromptTemplate(ContinuityCheck,
            system: "You are a continuity editor for a novel. You are given the author's character records, the story timeline, and " +
                    "chapter summaries in reading order. Find contradictions and slips: a character knowing something they cannot yet know, " +
                    "names or roles that change, timeline events told in an order that clashes with the summaries, places or objects that " +
                    "move without explanation, motivations that reverse without cause. For each finding give: what the contradiction is, " +
                    "the chapters involved (by number and title), and the smallest fix. If something only looks like a contradiction but a " +
                    "flashback or a deliberate reveal could explain it, say so. Number the findings; put the most serious first; " +
                    "end with a one-line verdict on overall continuity. If you find nothing, say so plainly.",
            user:   "Book: {book}\nScope: {scope}\n\nCharacters:\n{characters}\n\nTimeline (story order):\n{timeline}\n\nChapter summaries in reading order:\n{summaries}",
            description: "Contradictions across chapters, checked against the character records and timeline. Becomes an Analysis item on the section or the book.",
            maxTokens: 4000),

        new PromptTemplate(PlotAnalysis,
            system: "You are a story editor analysing structure from chapter summaries. Produce, with these exact headings: " +
                    "ACTS — how the chapters group into acts and where each turn falls; " +
                    "TENSION BY CHAPTER — one line per chapter as \"<number>. <title>: <score 1–10> — <why>\" in reading order; " +
                    "SLOW STRETCHES — runs of low tension and what could lift them; " +
                    "PLOTLINES — the threads you can see, and where each is picked up or dropped; " +
                    "SUGGESTED CONVERGENCES — pairs of threads that could meet, and in which chapter. " +
                    "Be concrete, use the chapter numbers, no praise padding.",
            user:   "Book: {book}\nScope: {scope}\n\nKnown plotlines:\n{plotlines}\n\nChapter summaries in reading order:\n{summaries}",
            description: "Acts, a tension score per chapter, slow stretches, plotlines and suggested convergences, from the chapter summaries. Becomes an Analysis item on the section or the book.",
            maxTokens: 4000),

        new PromptTemplate(StyleRead,
            system: "You are a line editor. You are given local statistics about a chapter's prose and the chapter itself. Describe the " +
                    "voice and tone in a paragraph; then, using the statistics as leads (not verdicts), point at specific habits with " +
                    "quoted examples: sentence rhythm, passive constructions worth turning active, adverbs that could go, repeated " +
                    "openings, dialogue balance, over-used words. End with five concrete line-level fixes. Plain headings, short paragraphs.",
            user:   "Book: {book}\nChapter: {chapter}\n\nStatistics:\n{stats}\n\nThe chapter:\n{text}",
            description: "An AI read of a chapter's voice and prose habits, guided by the local style statistics. Becomes an Analysis item.",
            maxTokens: 2500)
    };
}
