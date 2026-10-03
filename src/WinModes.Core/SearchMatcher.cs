using System.Globalization;

namespace WinModes.Core;

/// <summary>
/// The search of every list: the text is cut at spaces and each word must be found in at least one field, in any order,
/// so "xbox game" finds an entry that says "Xbox" in its title and "game" in its description.
/// </summary>
public static class SearchMatcher
{
    /// <summary>The words of a search text; empty when the text is blank.</summary>
    public static string[] Terms(string? query) =>
        string.IsNullOrWhiteSpace(query) ? [] : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>True when every word of <paramref name="query"/> is part of one of the fields. A blank query matches everything.</summary>
    public static bool Matches(string? query, params IEnumerable<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var terms = Terms(query);
        if (terms.Length == 0)
        {
            return true;
        }

        var texts = fields.Where(field => !string.IsNullOrEmpty(field)).ToList();
        return terms.All(term => texts.Any(text => CultureInfo.CurrentCulture.CompareInfo.IndexOf(text!, term, CompareOptions.IgnoreCase) >= 0));
    }

    /// <summary>Matches pre-parsed terms against up to five fields without rebuilding the term list for every row.</summary>
    public static bool MatchesTerms(IReadOnlyList<string> terms, string? first, string? second = null, string? third = null, string? fourth = null, string? fifth = null)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.Count == 0)
        {
            return true;
        }

        var compare = CultureInfo.CurrentCulture.CompareInfo;
        foreach (var term in terms)
        {
            if (!Contains(first, term, compare)
                && !Contains(second, term, compare)
                && !Contains(third, term, compare)
                && !Contains(fourth, term, compare)
                && !Contains(fifth, term, compare))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(string? value, string term, CompareInfo compare) =>
        value is not null && compare.IndexOf(value, term, CompareOptions.IgnoreCase) >= 0;
}
