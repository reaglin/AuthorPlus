using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Style;

namespace AuthorPlus.Tests;

public class StyleMetricsTests
{
    private const string Sample =
        "The message appeared on every screen. It was watched by millions. \"Two have been removed,\" it said quietly.\n\n" +
        "Roland read it slowly. Roland read it again. Roland frowned. He picked up the phone and dialled the number that he had been given by the editor the week before, a number he had never expected to need.\n\n" +
        "Nothing happened.";

    [Fact]
    public void Counts_words_sentences_paragraphs_and_lengths()
    {
        var r = StyleMetrics.Analyze(Sample);
        Assert.Equal(3, r.Paragraphs);
        Assert.Equal(8, r.Sentences);
        Assert.True(r.Words is > 55 and < 65, $"words={r.Words}");
        Assert.Equal(28, r.LongestSentenceWords);
        Assert.StartsWith("He picked up the phone", r.LongestSentence);
        Assert.Equal(8, r.SentenceLengthBuckets.Sum());
        Assert.True(r.AverageSentenceLength > 5);
    }

    [Fact]
    public void Flags_passive_voice_adverbs_dialogue_and_repeated_openings()
    {
        var r = StyleMetrics.Analyze(Sample);
        Assert.True(r.PassiveSentences >= 2, $"passive={r.PassiveSentences}");   // "was watched", "had been given"
        Assert.Equal(2, r.Adverbs);                                              // quietly, slowly ("every" is not an adverb)
        Assert.True(r.DialoguePercent > 5);
        Assert.Contains("roland", r.RepeatedSentenceStarts);
        Assert.Contains(r.TopWords, t => t.Word == "roland" && t.Count == 3);
        Assert.DoesNotContain(r.TopWords, t => t.Word == "the");
        Assert.True(r.FleschReadingEase is > 40 and < 100);
        Assert.Contains("Passive-voice sentences", StyleMetrics.Describe(r));
    }

    [Theory]
    [InlineData("cat", 1)]
    [InlineData("message", 2)]
    [InlineData("appeared", 2)]
    [InlineData("watched", 1)]
    [InlineData("needed", 2)]
    [InlineData("simultaneously", 5)]
    [InlineData("table", 2)]
    public void Syllable_estimate(string word, int expected) => Assert.Equal(expected, StyleMetrics.Syllables(word));

    [Fact]
    public void Empty_text_is_all_zeros()
    {
        var r = StyleMetrics.Analyze("");
        Assert.Equal(0, r.Words);
        Assert.Equal(0, r.Sentences);
        Assert.Equal(0, r.FleschReadingEase);
    }
}

public class TextChunkerTests
{
    [Fact]
    public void Short_text_is_one_chunk_and_empty_is_none()
    {
        Assert.Single(TextChunker.Split("short", 100));
        Assert.Empty(TextChunker.Split("", 100));
    }

    [Fact]
    public void Splits_at_paragraph_boundaries_without_losing_anything()
    {
        var paragraphs = Enumerable.Range(1, 10).Select(i => $"Paragraph {i} " + new string('x', 80)).ToList();
        var text = string.Join("\n\n", paragraphs);

        var chunks = TextChunker.Split(text, 300);

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, c => Assert.True(c.Length <= 300));
        Assert.All(chunks, c => Assert.StartsWith("Paragraph", c));
        Assert.Equal(text, string.Join("\n\n", chunks));
    }

    [Fact]
    public void A_paragraph_longer_than_the_limit_is_split_at_sentence_ends()
    {
        var text = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"Sentence number {i} is here."));
        var chunks = TextChunker.Split(text, 120);
        Assert.True(chunks.Count >= 4);
        Assert.All(chunks, c => Assert.True(c.Length <= 120));
        Assert.All(chunks, c => Assert.EndsWith(".", c));
        Assert.Equal(text.Replace(" ", ""), string.Concat(chunks).Replace(" ", "").Replace("\n", ""));
    }
}
