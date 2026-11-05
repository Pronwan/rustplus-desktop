using System;
using System.Linq;

namespace RustPlusDesk.Views;

internal static class SettingsSearchMatcher
{
    public static bool Matches(string query, params string[] values)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term => values.Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }
}
