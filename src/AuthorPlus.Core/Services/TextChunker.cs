namespace AuthorPlus.Core.Services;

/// <summary>
/// Splits long prose into pieces no longer than a limit, cutting only at paragraph boundaries
/// (blank lines) and, when a single paragraph is longer than the limit, at sentence ends.
/// Nothing is dropped: joining the pieces gives the text back. Used so a chapter that would
/// exceed a model's practical input is summarised in parts and never silently truncated.
/// </summary>
public static class TextChunker
{
    public static IReadOnlyList<string> Split(string text, int maxChars)
    {
        if (maxChars <= 0) throw new ArgumentOutOfRangeException(nameof(maxChars));
        text = (text ?? "").Replace("\r\n", "\n");
        if (text.Length <= maxChars) return text.Length == 0 ? Array.Empty<string>() : new[] { text };

        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var paragraph in text.Split("\n\n"))
        {
            foreach (var piece in SplitParagraph(paragraph, maxChars))
            {
                var extra = current.Length == 0 ? piece.Length : piece.Length + 2;
                if (current.Length > 0 && current.Length + extra > maxChars)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) current.Append("\n\n");
                current.Append(piece);
            }
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    private static IEnumerable<string> SplitParagraph(string paragraph, int maxChars)
    {
        if (paragraph.Length <= maxChars) { yield return paragraph; yield break; }
        var sb = new System.Text.StringBuilder();
        int start = 0;
        for (int i = 0; i < paragraph.Length; i++)
        {
            bool sentenceEnd = paragraph[i] is '.' or '!' or '?' && (i + 1 == paragraph.Length || char.IsWhiteSpace(paragraph[i + 1]));
            if (!sentenceEnd && i + 1 != paragraph.Length) continue;
            var sentence = paragraph[start..(i + 1)];
            start = i + 1;
            if (sb.Length > 0 && sb.Length + sentence.Length > maxChars) { yield return sb.ToString().Trim(); sb.Clear(); }
            if (sentence.Length > maxChars)                   // a sentence longer than the limit: hard cut, still nothing lost
            {
                for (int k = 0; k < sentence.Length; k += maxChars) yield return sentence.Substring(k, Math.Min(maxChars, sentence.Length - k)).Trim();
                continue;
            }
            sb.Append(sentence);
        }
        if (sb.Length > 0) yield return sb.ToString().Trim();
    }
}
