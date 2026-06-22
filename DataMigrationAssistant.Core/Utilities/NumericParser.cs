using System.Globalization;

namespace DataMigrationAssistant.Core.Utilities;

/// <summary>
/// Culture-aware decimal parsing that normalises all values to InvariantCulture internally.
/// Supports both dot ("9.5") and comma ("9,5") as decimal separators.
/// Group separators are intentionally excluded to prevent "9,5" from being silently
/// treated as 95 under InvariantCulture (where comma is the thousands separator).
/// </summary>
internal static class NumericParser
{
    private static readonly NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
        NumberStyles.AllowLeadingSign  | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// Parses a decimal value supporting both "9.5" (dot) and "9,5" (comma as decimal separator).
    /// When a comma is present and no dot exists, the comma is treated as the decimal point.
    /// </summary>
    public static bool TryParseDecimal(string value, out decimal result)
    {
        if (decimal.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out result))
            return true;

        if (value.Contains(',') && !value.Contains('.'))
        {
            var normalized = value.Replace(',', '.');
            if (decimal.TryParse(normalized, DecimalStyles, CultureInfo.InvariantCulture, out result))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the InvariantCulture string representation of <paramref name="value"/> if it
    /// is a parseable decimal, or null when it is not.
    /// </summary>
    public static string? ToInvariantString(string value)
        => TryParseDecimal(value, out var d) ? d.ToString(CultureInfo.InvariantCulture) : null;
}
