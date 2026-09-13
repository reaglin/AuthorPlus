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
    public const string AnalysisSuggestions = "analysis-suggestions";
    public const string ChapterExtract      = "chapter-extract";

    /// <summary>
    /// Practical ceiling for one request's prose, in characters (≈ 100k tokens). Above it a
    /// chapter is summarised in parts (<see cref="ChunkSummary"/> then <see cref="MergeSummaries"/>)
    /// and a book-level prompt is refused with an explanation — never cut off silently.
    /// </summary>
    public const int MaxPromptChars = 400_000;

    public static IReadOnlyList<PromptTemplate> Defaults { get; } = new[]
    {
        new PromptTemplate(ChapterSummary,
            system: "You are an editorial assistant for a novelist. Summarize the chapter for the author's own records, in this exact shape:\n\n" +
                    "What happens\n- one bullet per event or turn, in order (3–8 bullets, each one sentence)\n\n" +
                    "Characters\n- Name (introduced here): what they do in this chapter — only for characters who first appear in this chapter\n" +
                    "- Name: what they do in this chapter\n\n" +
                    "Use the two headings exactly as written, one bullet per line, a blank line between the two lists. " +
                    "Plain prose in each bullet, no praise, no critique, nothing not in the text.",
            user:   "Book: {book}\nChapter: {chapter}\n\n{text}",
            description: "What happens (bulleted, in order) and what each character does, marking who is introduced. Becomes the chapter's Summary item.",
            maxTokens: 1200),

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
                    "Write the analysis under these headings, each on its own line exactly as \"## Heading\", in this order: " +
                    "## Pacing, ## Tension, ## Stakes, ## Point of view and voice, ## Dialogue, ## Continuity, ## Prose habits, ## Three changes. " +
                    "Under each heading give 2–5 short paragraphs or bullets; quote short phrases from the text when you point at something; " +
                    "Continuity means clarity and consistency with what came before (the earlier summaries). " +
                    "Under Three changes give the three changes that would help most, numbered. No praise padding; nothing outside the headings.",
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

        new PromptTemplate(AnalysisSuggestions,
            system: "You are a line editor helping a novelist act on one point from an editorial analysis of a chapter. " +
                    "For the aspect named, give 3–6 concrete rewrite suggestions. For each: the passage concerned (quote it briefly, or name where it is), " +
                    "a rewritten version of that passage in the author's own voice, and one sentence on why it helps. " +
                    "Number the suggestions. Do not rewrite the whole chapter; do not add plot the text does not have.",
            user:   "Book: {book}\nChapter: {chapter}\nAspect: {aspect}\n\nWhat the analysis said about it:\n{finding}\n\nThe chapter:\n{text}",
            description: "Rewrite suggestions for one aspect of a chapter analysis (the Suggestions… button on an analysis card). Saved as a Suggestions item under the analysis.",
            maxTokens: 2500),

        new PromptTemplate(ChapterExtract,
            system: "You extract records for an author's story bible from one chapter. Output exactly two blocks and nothing else:\n\n" +
                    "CHARACTERS\nName | known or new | POV or - | what they do in this chapter (one short clause)\n\n" +
                    "PLOTLINES\nName | known or new | what happens in this thread in this chapter (one short clause)\n\n" +
                    "One line per character who appears or acts (not a passing mention); one line per story thread the chapter advances. " +
                    "Use the known names exactly when they match (an alias counts as known); mark anyone or anything not in the known lists as new. " +
                    "Give a new plotline a short descriptive name (2–5 words). Mark exactly one character POV when the chapter has a viewpoint character.",
            user:   "Chapter: {chapter}\n\nKnown characters (name — also called):\n{known_characters}\n\nKnown plotlines:\n{known_plotlines}\n\n{text}",
            description: "Runs after Analyze…: lists the chapter's characters and plotlines so they can be linked, and new ones added to the book.",
            maxTokens: 1200),

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
