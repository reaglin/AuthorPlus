namespace AuthorPlus.Core.Services;

/// <summary>One definition of "a word" for the whole app: runs of non-whitespace.</summary>
public static class WordCounter
{
    public static int Count(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int n = 0; bool inWord = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) inWord = false;
            else if (!inWord) { inWord = true; n++; }
        }
        return n;
    }
}
