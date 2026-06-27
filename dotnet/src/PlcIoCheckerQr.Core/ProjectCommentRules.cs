using System.Text;

namespace PlcIoCheckerQr.Core;

public static class ProjectCommentRules
{
    public const int MaxCommentCharacters = 1024;

    public static string Normalize(string text)
    {
        var normalized = text
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        Validate(normalized);
        return normalized;
    }

    public static void Validate(string comment)
    {
        if (comment.EnumerateRunes().Count() > MaxCommentCharacters)
        {
            throw new ArgumentException($"Comment exceeds {MaxCommentCharacters} characters.");
        }
    }
}
