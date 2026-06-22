using System.Globalization;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Utilities;

namespace DataMigrationAssistant.Core.Inference;

internal static class TypeInferrer
{
    private static readonly HashSet<string> BooleanStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "y", "n", "t", "f"
    };

    // Explicit date-only formats prevent DateOnly.TryParse from accepting ISO 8601 datetime
    // strings (e.g. "2023-01-15T10:30:00") by extracting the date part and discarding the time.
    private static readonly string[] DateOnlyFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd",
        "dd-MM-yyyy", "dd/MM/yyyy",
        "MM-dd-yyyy", "MM/dd/yyyy",
    ];

    /// <summary>
    /// Returns the most specific PostgreSQL type for a single non-null, non-empty value.
    /// "1"/"0" classify as Integer (not Boolean) to avoid accidental bool coercion.
    /// </summary>
    public static PostgresType ClassifyValue(string value)
    {
        if (BooleanStrings.Contains(value))
            return PostgresType.Boolean;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return PostgresType.Integer;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return PostgresType.BigInt;

        if (NumericParser.TryParseDecimal(value, out _))
            return PostgresType.Numeric;

        if (DateOnly.TryParseExact(value, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return PostgresType.Date;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return PostgresType.Timestamp;

        return PostgresType.Text;
    }

    /// <summary>
    /// Infers the column type by classifying each non-null value and promoting to the
    /// least specific type that accommodates all values. Null/empty values are ignored.
    /// Returns Text if all values are null or the collection is empty.
    /// </summary>
    public static PostgresType InferColumnType(IEnumerable<string?> values)
    {
        PostgresType? current = null;

        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var valueType = ClassifyValue(raw.Trim());
            current = current is null ? valueType : Promote(current.Value, valueType);

            if (current == PostgresType.Text) break;
        }

        return current ?? PostgresType.Text;
    }

    /// <summary>
    /// Returns the less specific of two types. Types in the same domain promote within
    /// that domain; types from incompatible domains (e.g. Integer + Date) resolve to Text.
    /// </summary>
    public static PostgresType Promote(PostgresType a, PostgresType b)
    {
        if (a == b) return a;
        if (a == PostgresType.Text || b == PostgresType.Text) return PostgresType.Text;

        int aRank = (int)a;
        int bRank = (int)b;

        bool bothNumeric  = aRank < 10 && bRank < 10;
        bool bothTemporal = aRank is >= 10 and < 99 && bRank is >= 10 and < 99;

        if (bothNumeric || bothTemporal)
            return (PostgresType)Math.Max(aRank, bRank);

        return PostgresType.Text;
    }
}
