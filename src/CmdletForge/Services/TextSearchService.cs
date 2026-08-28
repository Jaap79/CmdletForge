using System.Text.RegularExpressions;

namespace CmdletForge.Services;

public sealed record SearchOptions(bool MatchCase, bool WholeWord, bool UseRegex);
public sealed record TextMatch(int Index, int Length, string Value);

public static class TextSearchService
{
    public static Regex BuildRegex(string searchText, SearchOptions options)
    {
        if (string.IsNullOrEmpty(searchText))
            throw new ArgumentException("Zoektekst mag niet leeg zijn.", nameof(searchText));

        var pattern = options.UseRegex ? searchText : Regex.Escape(searchText);
        if (options.WholeWord)
            pattern = $@"\b(?:{pattern})\b";
        var regexOptions = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
            regexOptions |= RegexOptions.IgnoreCase;
        return new Regex(pattern, regexOptions, TimeSpan.FromSeconds(1));
    }

    public static IReadOnlyList<TextMatch> FindAll(string text, Regex regex) =>
        regex.Matches(text).Cast<Match>()
            .Where(match => match.Length > 0)
            .Select(match => new TextMatch(match.Index, match.Length, match.Value))
            .ToArray();
}
