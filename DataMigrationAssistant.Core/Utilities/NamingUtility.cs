using System.Text.RegularExpressions;

namespace DataMigrationAssistant.Core.Utilities;

internal static class NamingUtility
{
    public static string ToSnakeCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "column";

        var result = input.Trim();
        result = Regex.Replace(result, @"([a-z0-9])([A-Z])", "$1_$2");
        result = Regex.Replace(result, @"[^a-zA-Z0-9]", "_");
        result = Regex.Replace(result, @"_+", "_").Trim('_');
        return result.ToLowerInvariant();
    }
}
