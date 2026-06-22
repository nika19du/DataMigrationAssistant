using System.Globalization;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Utilities;

namespace DataMigrationAssistant.Core.Generators;

/// <summary>
/// Converts raw cell strings to their PostgreSQL literal representation.
/// Null/whitespace always produces NULL. All other conversions are type-driven.
/// </summary>
internal static class SqlValueFormatter
{
    private static readonly HashSet<string> TrueValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "yes", "y", "t",
    };

    private static readonly HashSet<string> FalseValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "false", "no", "n", "f",
    };

    public static string Format(string? rawValue, PostgresType type)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return "NULL";

        var value = rawValue.Trim();

        return type switch
        {
            PostgresType.Boolean   => FormatBoolean(value),
            PostgresType.Integer   => FormatInteger(value),
            PostgresType.BigInt    => FormatBigInt(value),
            PostgresType.Numeric   => FormatNumeric(value),
            PostgresType.Date      => FormatDate(value),
            PostgresType.Timestamp => FormatTimestamp(value),
            PostgresType.Text      => FormatText(value),
            _                      => FormatText(value),
        };
    }

    private static string FormatBoolean(string value)
    {
        if (TrueValues.Contains(value))  return "TRUE";
        if (FalseValues.Contains(value)) return "FALSE";
        return FormatText(value);
    }

    private static string FormatInteger(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i.ToString(CultureInfo.InvariantCulture)
            : FormatText(value);
    }

    private static string FormatBigInt(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
            ? l.ToString(CultureInfo.InvariantCulture)
            : FormatText(value);
    }

    private static string FormatNumeric(string value)
    {
        return NumericParser.TryParseDecimal(value, out var d)
            ? d.ToString(CultureInfo.InvariantCulture)
            : FormatText(value);
    }

    private static string FormatDate(string value)
    {
        // Use DateTime.TryParse so both "2023-01-15" and locale variants are normalised to yyyy-MM-dd.
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? $"'{dt:yyyy-MM-dd}'"
            : FormatText(value);
    }

    private static string FormatTimestamp(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? $"'{dt:yyyy-MM-dd HH:mm:ss}'"
            : FormatText(value);
    }

    // Single-quote escaping: replace ' with '' per the SQL standard.
    private static string FormatText(string value) => $"'{value.Replace("'", "''")}'";
}
