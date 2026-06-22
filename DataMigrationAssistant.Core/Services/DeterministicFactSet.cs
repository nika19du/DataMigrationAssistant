using DataMigrationAssistant.Core.Models;
using System.Text.RegularExpressions;

namespace DataMigrationAssistant.Core.Services;

/// <summary>
/// Typed index over Schema + Validation + Data Analysis.
/// Built once per request and passed to contradiction rules for O(1) authoritative fact lookups.
/// </summary>
internal sealed class DeterministicFactSet
{
    private static readonly Regex PkDescriptionPattern =
        new(@"Designate\s+'(\w+)'\s+as\s+the\s+PRIMARY\s+KEY", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IReadOnlyList<ColumnSchema> _columns;
    private readonly IReadOnlyList<ValidationWarning> _warnings;
    private readonly IReadOnlyList<IReadOnlyDictionary<string, string?>> _rows;
    private readonly DataAnalysisResult? _analysis;

    /// <summary>Column name of the DA-recommended primary key, or null if none.</summary>
    public string? RecommendedPrimaryKey { get; }

    /// <summary>True when Data Analysis ran and produced a non-empty summary.</summary>
    public bool DataAnalysisRan { get; }

    private DeterministicFactSet(
        TableSchema schema,
        ValidationResult validation,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        DataAnalysisResult? analysis)
    {
        _columns  = schema.Columns;
        _warnings = validation.Warnings;
        _rows     = rows;
        _analysis = analysis;
        DataAnalysisRan       = analysis is not null && !string.IsNullOrWhiteSpace(analysis.Summary);
        RecommendedPrimaryKey = ExtractRecommendedPk(analysis);
    }

    public static DeterministicFactSet Build(AiReviewRequest request)
        => new(request.TableSchema, request.ValidationResult,
               request.SheetPreview.Rows, request.DataAnalysisResult);

    // ── Column resolution ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds the column schema. Tries <paramref name="columnHint"/> first (explicit AI column field),
    /// then falls back to word-boundary scanning of the combined description + evidence text.
    /// </summary>
    public ColumnSchema? ResolveColumn(string description, string? evidence, string? columnHint)
    {
        if (!string.IsNullOrWhiteSpace(columnHint))
        {
            var exact = GetColumn(columnHint);
            if (exact is not null) return exact;
        }

        var combined = $"{description} {evidence}".ToLowerInvariant();
        return FindColumnWithWordBoundary(combined);
    }

    public ColumnSchema? GetColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return null;
        return _columns.FirstOrDefault(c =>
            c.SnakeCaseName.Equals(columnName, StringComparison.OrdinalIgnoreCase) ||
            c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
    }

    // ── Schema facts ──────────────────────────────────────────────────────────

    public PostgresType? TypeOf(string columnName)
        => GetColumn(columnName)?.InferredType;

    public bool IsNullable(string columnName)
        => GetColumn(columnName)?.IsNullable ?? false;

    public bool HasSchemaCandidate(string columnName)
        => GetColumn(columnName)?.IsCandidateKey ?? false;

    // ── Data Analysis facts ───────────────────────────────────────────────────

    /// <summary>DA produced an explicit DuplicateRisk finding for this column.</summary>
    public bool HasDuplicateRisk(string columnName)
    {
        if (!DataAnalysisRan) return false;
        return _analysis!.Risks.Any(r =>
            r.Category == "DuplicateRisk" &&
            r.Description.Contains(columnName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>DA produced an explicit NullableRisk finding for this column.</summary>
    public bool HasNullableRisk(string columnName)
    {
        if (!DataAnalysisRan) return false;
        return _analysis!.Risks.Any(r =>
            r.Category == "NullableRisk" &&
            r.Description.Contains(columnName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>DA finding says this column is not recommended as a key.</summary>
    public bool IsNotRecommendedKey(string columnName)
    {
        if (!DataAnalysisRan) return false;
        return _analysis!.Findings.Any(f =>
            f.Category == "CandidateKey" &&
            f.Description.Contains("not recommended as a key", StringComparison.OrdinalIgnoreCase) &&
            f.Description.Contains(columnName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>DA finding detail explicitly labels this column as a numeric value column.</summary>
    public bool IsNumericValueColumn(string columnName)
    {
        if (!DataAnalysisRan) return false;
        return _analysis!.Findings.Any(f =>
            f.Category == "CandidateKey" &&
            f.Description.Contains(columnName, StringComparison.OrdinalIgnoreCase) &&
            f.Detail != null &&
            f.Detail.Contains("numeric value column", StringComparison.OrdinalIgnoreCase));
    }

    // ── Composite evidence checks ─────────────────────────────────────────────

    /// <summary>
    /// True when any legitimate nullability evidence exists:
    /// schema marks column nullable, an actual NULL value appears in rows,
    /// or a validation warning mentions nulls/missing values for this column.
    /// Returns true for unknown columns (allow the claim through).
    /// </summary>
    public bool HasNullabilityEvidence(string columnName)
    {
        var col = GetColumn(columnName);
        if (col is null) return true;
        if (col.IsNullable) return true;
        if (_rows.Any(r => r.TryGetValue(col.SnakeCaseName, out var v) && v is null))
            return true;
        return _warnings.Any(w =>
            (w.ColumnName == null ||
             w.ColumnName.Equals(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase)) &&
            (w.Message.Contains("null", StringComparison.OrdinalIgnoreCase) ||
             w.Message.Contains("missing", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// True when any legitimate duplicate evidence exists:
    /// DA DuplicateRisk, a validation warning about duplicates, or actual duplicates in rows.
    /// Returns true for unknown columns (allow the claim through).
    /// </summary>
    public bool HasDuplicateEvidence(string columnName)
    {
        if (HasDuplicateRisk(columnName)) return true;

        var col = GetColumn(columnName);
        if (col is null) return true;

        if (_warnings.Any(w =>
            w.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
            w.ColumnName != null &&
            w.ColumnName.Equals(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase)))
            return true;

        var values = _rows
            .Select(r => r.TryGetValue(col.SnakeCaseName, out var v) ? v : null)
            .Where(v => v != null)
            .ToList();
        return values.Count != values.Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string? ExtractRecommendedPk(DataAnalysisResult? analysis)
    {
        if (analysis is null) return null;
        var pkRec = analysis.Recommendations.FirstOrDefault(r =>
            r.Type == "PrimaryKey" &&
            !r.Description.Contains("surrogate", StringComparison.OrdinalIgnoreCase));
        if (pkRec is null) return null;
        var m = PkDescriptionPattern.Match(pkRec.Description);
        return m.Success ? m.Groups[1].Value : null;
    }

    private ColumnSchema? FindColumnWithWordBoundary(string combined)
    {
        foreach (var col in _columns)
        {
            var name = col.SnakeCaseName.ToLowerInvariant();
            if (!combined.Contains(name)) continue;

            var idx = 0;
            while ((idx = combined.IndexOf(name, idx, StringComparison.Ordinal)) >= 0)
            {
                var beforeOk = idx == 0 || !char.IsLetterOrDigit(combined[idx - 1]);
                var afterOk  = (idx + name.Length) >= combined.Length ||
                               !char.IsLetterOrDigit(combined[idx + name.Length]);
                if (beforeOk && afterOk) return col;
                idx++;
            }
        }
        return null;
    }
}
