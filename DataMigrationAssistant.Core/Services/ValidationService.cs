using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class ValidationService : IValidationService
{
    private const double HighNullRatioThreshold   = 0.5;
    private const double MixedTypeNumericThreshold = 0.5;

    private static readonly (Regex Pattern, string Name)[] DatePatterns =
    [
        (new Regex(@"^\d{4}-\d{2}-\d{2}$",                RegexOptions.Compiled), "yyyy-MM-dd"),
        (new Regex(@"^\d{4}/\d{2}/\d{2}$",                RegexOptions.Compiled), "yyyy/MM/dd"),
        (new Regex(@"^\d{2}-\d{2}-\d{4}$",                RegexOptions.Compiled), "dd-MM-yyyy"),
        (new Regex(@"^\d{2}/\d{2}/\d{4}$",                RegexOptions.Compiled), "dd/MM/yyyy"),
        (new Regex(@"^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}", RegexOptions.Compiled), "ISO timestamp"),
    ];

    public ValidationResult Validate(SheetPreview preview, TableSchema schema)
    {
        // EMPTY_SHEET — processing is impossible without columns
        if (preview.Columns.Count == 0)
            return new ValidationResult
            {
                CanProceed = false,
                Warnings =
                [
                    new ValidationWarning
                    {
                        Code     = "EMPTY_SHEET",
                        Severity = ValidationSeverity.Warning,
                        Message  = $"Sheet '{preview.SheetName}' has no columns and cannot be processed.",
                    },
                ],
            };

        var warnings = new List<ValidationWarning>();

        // NO_DATA_ROWS — sheet has columns but no rows
        if (preview.TotalRowCount == 0)
        {
            warnings.Add(new ValidationWarning
            {
                Code     = "NO_DATA_ROWS",
                Severity = ValidationSeverity.Info,
                Message  = $"Sheet '{preview.SheetName}' has no data rows. Generated SQL will be empty.",
            });
        }

        if (preview.TotalRowCount > 0)
        {
            // NO_CANDIDATE_KEY — upsert and diff operations require a unique non-null column
            if (schema.Columns.All(c => !c.IsCandidateKey))
            {
                warnings.Add(new ValidationWarning
                {
                    Code       = "NO_CANDIDATE_KEY",
                    Severity   = ValidationSeverity.Warning,
                    Message    = $"No candidate key column found in '{schema.TableName}'. " +
                                 "UPSERT and diff operations require a unique, non-null column.",
                    Suggestion = "Add a surrogate id column or configure a stable business key before generating SQL.",
                });
            }

            // MULTIPLE_CANDIDATE_KEYS — rank by quality then index; best one will be selected
            var candidateKeys = schema.Columns.Where(c => c.IsCandidateKey).ToList();
            if (candidateKeys.Count > 1)
            {
                var sorted = candidateKeys
                    .OrderByDescending(c => (int)c.CandidateKeyQuality)
                    .ThenBy(c => c.Index)
                    .ToList();

                var best        = sorted[0];
                var labeledList = string.Join(", ", sorted.Select(c => $"{c.SnakeCaseName} ({FormatKeyQuality(c.CandidateKeyQuality)})"));
                var suggestion  = BuildMultipleKeysSuggestion(best, sorted.Skip(1));

                warnings.Add(new ValidationWarning
                {
                    Code       = "MULTIPLE_CANDIDATE_KEYS",
                    Severity   = ValidationSeverity.Info,
                    Message    = $"Multiple candidate key columns detected in '{schema.TableName}': {labeledList}. " +
                                 $"'{best.SnakeCaseName}' will be selected as the key.",
                    Suggestion = suggestion,
                });
            }
        }

        // Per-column checks are only meaningful when there are rows to inspect
        if (preview.Rows.Count > 0)
        {
            foreach (var col in schema.Columns)
                CheckColumn(col, preview.Rows, warnings);
        }

        return new ValidationResult
        {
            CanProceed = true,
            Warnings   = warnings,
        };
    }

    private static void CheckColumn(
        ColumnSchema col,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        List<ValidationWarning> warnings)
    {
        var values = rows
            .Select(r => r.TryGetValue(col.SnakeCaseName, out var v) ? v : null)
            .ToList();

        var nonNull = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        var nullCount = values.Count - nonNull.Count;

        // ALL_NULL_COLUMN — column carries no data at all
        if (nonNull.Count == 0)
        {
            warnings.Add(new ValidationWarning
            {
                Code       = "ALL_NULL_COLUMN",
                Severity   = ValidationSeverity.Warning,
                Message    = $"Column '{col.SnakeCaseName}' contains only null or empty values.",
                ColumnName = col.SnakeCaseName,
            });
            return; // remaining checks need at least one non-null value
        }

        // HIGH_NULL_RATIO — more than half the rows are missing data for this column
        if (nullCount > 0)
        {
            var ratio = (double)nullCount / values.Count;
            if (ratio >= HighNullRatioThreshold)
            {
                warnings.Add(new ValidationWarning
                {
                    Code       = "HIGH_NULL_RATIO",
                    Severity   = ValidationSeverity.Warning,
                    Message    = $"Column '{col.SnakeCaseName}' has {ratio:P0} null values ({nullCount} of {values.Count} rows).",
                    ColumnName = col.SnakeCaseName,
                });
            }
        }

        // DUPLICATE_VALUES — non-nullable numeric column has duplicates, preventing key use
        if (!col.IsNullable && col.HasDuplicates
            && col.InferredType is PostgresType.Integer or PostgresType.BigInt)
        {
            warnings.Add(new ValidationWarning
            {
                Code       = "DUPLICATE_VALUES",
                Severity   = ValidationSeverity.Warning,
                Message    = $"Column '{col.SnakeCaseName}' has duplicate values and cannot serve as a primary key.",
                ColumnName = col.SnakeCaseName,
            });
        }

        // NULLABLE_KEY_CANDIDATE — unique numeric column with nulls; the nulls are the only obstacle
        if (col.IsNullable && !col.HasDuplicates
            && col.InferredType is PostgresType.Integer or PostgresType.BigInt)
        {
            warnings.Add(new ValidationWarning
            {
                Code       = "NULLABLE_KEY_CANDIDATE",
                Severity   = ValidationSeverity.Info,
                Message    = $"Column '{col.SnakeCaseName}' has unique values but contains nulls, " +
                             "preventing it from being used as a primary key.",
                ColumnName = col.SnakeCaseName,
            });
        }

        // MIXED_TYPES — column inferred as Text but the majority of non-null values are numeric
        if (col.InferredType == PostgresType.Text)
        {
            var numericCount = nonNull.Count(v =>
                decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _));

            if (numericCount > 0 && numericCount < nonNull.Count)
            {
                var numericRatio = (double)numericCount / nonNull.Count;
                if (numericRatio >= MixedTypeNumericThreshold)
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code       = "MIXED_TYPES",
                        Severity   = ValidationSeverity.Warning,
                        Message    = $"Column '{col.SnakeCaseName}' was inferred as TEXT but " +
                                     $"{numericCount} of {nonNull.Count} non-null values are numeric. " +
                                     "Check for mixed data.",
                        ColumnName = col.SnakeCaseName,
                    });
                }
            }
        }

        // MIXED_DATE_FORMATS — date/timestamp column whose values use more than one format pattern
        if (col.InferredType is PostgresType.Date or PostgresType.Timestamp)
        {
            var formats = nonNull
                .Select(DetectDateFormat)
                .OfType<string>()
                .Distinct()
                .ToList();

            if (formats.Count > 1)
            {
                warnings.Add(new ValidationWarning
                {
                    Code       = "MIXED_DATE_FORMATS",
                    Severity   = ValidationSeverity.Warning,
                    Message    = $"Column '{col.SnakeCaseName}' contains dates in multiple formats: {string.Join(", ", formats)}.",
                    ColumnName = col.SnakeCaseName,
                });
            }
        }

        // SUSPICIOUS_TYPE — column name implies a numeric ID or temporal type but was inferred as Text
        if (col.InferredType == PostgresType.Text)
        {
            var name = col.SnakeCaseName;

            bool looksLikeId = name == "id"
                || name.EndsWith("_id",  StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_key", StringComparison.OrdinalIgnoreCase);

            bool looksLikeTemporal =
                name.EndsWith("_at",   StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_on", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_date", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_time", StringComparison.OrdinalIgnoreCase)
                || name is "date" or "timestamp";

            if (looksLikeId)
            {
                warnings.Add(new ValidationWarning
                {
                    Code       = "SUSPICIOUS_TYPE",
                    Severity   = ValidationSeverity.Warning,
                    Message    = $"Column '{col.SnakeCaseName}' appears to be an identifier but was inferred as TEXT. " +
                                 "Check for non-numeric or mixed values.",
                    ColumnName = col.SnakeCaseName,
                });
            }
            else if (looksLikeTemporal)
            {
                warnings.Add(new ValidationWarning
                {
                    Code       = "SUSPICIOUS_TYPE",
                    Severity   = ValidationSeverity.Warning,
                    Message    = $"Column '{col.SnakeCaseName}' appears to be a date/time column but was inferred as TEXT. " +
                                 "Check for inconsistent date formats or non-date values.",
                    ColumnName = col.SnakeCaseName,
                });
            }
        }
    }

    private static string BuildMultipleKeysSuggestion(ColumnSchema best, IEnumerable<ColumnSchema> others)
    {
        var sb = new StringBuilder();
        sb.Append($"Use '{best.SnakeCaseName}' as PRIMARY KEY.");

        foreach (var c in others)
        {
            if (c.CandidateKeyQuality >= CandidateKeyQuality.Plausible)
                sb.Append($" Consider UNIQUE({c.SnakeCaseName}) only if {c.SnakeCaseName}s must be business-unique.");
            else
                sb.Append($" Do not use {c.SnakeCaseName} as a key because {KeyRejectionReason(c)}.");
        }

        return sb.ToString();
    }

    private static string FormatKeyQuality(CandidateKeyQuality quality) =>
        quality == CandidateKeyQuality.None ? "not recommended" : quality.ToString();

    private static string KeyRejectionReason(ColumnSchema col) =>
        col.InferredType switch
        {
            PostgresType.Boolean   => "it can only hold two distinct values",
            PostgresType.Numeric   => "it is a numeric value column",
            PostgresType.Date      => "it is a date column",
            PostgresType.Timestamp => "it is a timestamp column",
            _                      => "its name does not suggest a stable database key",
        };

    private static string? DetectDateFormat(string value)
    {
        foreach (var (pattern, name) in DatePatterns)
        {
            if (pattern.IsMatch(value))
                return name;
        }
        return null;
    }
}
