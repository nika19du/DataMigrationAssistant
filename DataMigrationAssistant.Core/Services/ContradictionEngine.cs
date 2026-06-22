using DataMigrationAssistant.Core.Models;
using System.Text.RegularExpressions;

namespace DataMigrationAssistant.Core.Services;

// ── Contradiction rule interface ───────────────────────────────────────────────

/// <summary>
/// A single contradiction rule.  Returns true when the AI claim directly contradicts
/// an authoritative deterministic fact, meaning the claim should be filtered.
/// </summary>
internal interface IContradictionRule
{
    bool Contradicts(
        string description,
        string? evidence,
        string? column,
        string levelOrPriority,
        DeterministicFactSet facts);
}

// ── Rule implementations ───────────────────────────────────────────────────────

/// <summary>
/// Filters "stored as text", "text storage", "non-numeric", "boolean conversion", and
/// similar wrong-type claims for columns whose schema already inferred NUMERIC or BOOLEAN.
/// </summary>
internal sealed class TypeStoredAsTextRule : IContradictionRule
{
    private static readonly string[] Phrases =
    [
        "stored as text", "text storage", "non-numeric",
        "boolean conversion", "convert to boolean",
        "is stored as text",
    ];

    public bool Contradicts(string description, string? evidence, string? column,
        string levelOrPriority, DeterministicFactSet facts)
    {
        var col = facts.ResolveColumn(description, evidence, column);
        if (col is null) return false;
        if (col.InferredType != PostgresType.Numeric && col.InferredType != PostgresType.Boolean)
            return false;

        var combined = $"{description} {evidence}".ToLowerInvariant();
        return Phrases.Any(p => combined.Contains(p));
    }
}

/// <summary>
/// Filters type inference risk claims ("incorrect type inference", "type mismatch", etc.)
/// for columns whose schema already correctly inferred NUMERIC or BOOLEAN.
/// Also filters such claims when DA explicitly marks the column as a numeric value column.
/// </summary>
internal sealed class TypeInferenceRule : IContradictionRule
{
    private static readonly string[] Phrases =
    [
        "type inference", "incorrect type inference", "inconsistent data types",
        "type mismatch", "type conversion issue", "data type inconsistency",
        "ambiguous numeric", "inferred incorrectly",
        "wrong type", "incorrect type",
    ];

    public bool Contradicts(string description, string? evidence, string? column,
        string levelOrPriority, DeterministicFactSet facts)
    {
        var col = facts.ResolveColumn(description, evidence, column);
        if (col is null) return false;
        if (col.InferredType != PostgresType.Numeric && col.InferredType != PostgresType.Boolean)
            return false;

        var combined = $"{description} {evidence}".ToLowerInvariant();
        if (Phrases.Any(p => combined.Contains(p))) return true;

        // DA marks column as a numeric value column + claim mentions type or format risk
        if (facts.IsNumericValueColumn(col.SnakeCaseName))
        {
            return combined.Contains("type inference") ||
                   combined.Contains("type risk") ||
                   combined.Contains("ambiguous format") ||
                   combined.Contains("culture-specific");
        }

        return false;
    }
}

/// <summary>
/// Filters "cast to numeric" / "convert to numeric" recommendations when the column
/// is already inferred as NUMERIC — the cast is redundant.
/// </summary>
internal sealed class CastToNumericRule : IContradictionRule
{
    public bool Contradicts(string description, string? evidence, string? column,
        string levelOrPriority, DeterministicFactSet facts)
    {
        var col = facts.ResolveColumn(description, evidence, column);
        if (col is null) return false;
        if (col.InferredType != PostgresType.Numeric) return false;

        var combined = $"{description} {evidence}".ToLowerInvariant();
        return (combined.Contains("cast") || combined.Contains("convert")) &&
               combined.Contains("to numeric");
    }
}

/// <summary>
/// Filters nullability claims when no legitimate nullability evidence exists:
/// column must be nullable in schema, have an actual NULL in rows, or have a validation
/// warning about missing values.  Also filters when the AI's own evidence field shows
/// only non-null values while the description claims a null/missing issue.
/// </summary>
internal sealed class NullabilityRule : IContradictionRule
{
    public bool Contradicts(string description, string? evidence, string? column,
        string levelOrPriority, DeterministicFactSet facts)
    {
        var combined = $"{description} {evidence}".ToLowerInvariant();

        // Match the same classification used by ClassifyClaim: "null" present but not negated
        bool isNullabilityClaim = combined.Contains("null") &&
                                  !combined.Contains("not null") &&
                                  !combined.Contains("nonnull") &&
                                  !combined.Contains("non-null");
        if (!isNullabilityClaim) return false;

        var col = facts.ResolveColumn(description, evidence, column);
        if (col is null) return false;

        // No nullability evidence anywhere → contradict
        if (!facts.HasNullabilityEvidence(col.SnakeCaseName))
            return true;

        // The AI's own evidence field shows only non-null values while claiming nullability
        if (EvidenceShowsOnlyNonNullValues(col.SnakeCaseName, evidence))
            return true;

        return false;
    }

    private static bool EvidenceShowsOnlyNonNullValues(string columnName, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return false;
        var pattern = new Regex(
            $@"\b{Regex.Escape(columnName)}\s*=\s*([^,;\s]+)",
            RegexOptions.IgnoreCase);
        var matches = pattern.Matches(evidence);
        if (matches.Count == 0) return false;
        return matches.All(m =>
            !m.Groups[1].Value.Equals("NULL", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Filters duplicate-risk claims when:
/// — Data Analysis ran and found no duplicate risk for this column (DA is authoritative), or
/// — Schema marks the column as a candidate key and no duplicate evidence exists in rows or warnings,
/// — The AI's own evidence field shows all-unique values while claiming duplicates.
/// </summary>
internal sealed class DuplicateRule : IContradictionRule
{
    public bool Contradicts(string description, string? evidence, string? column,
        string levelOrPriority, DeterministicFactSet facts)
    {
        var combined = $"{description} {evidence}".ToLowerInvariant();
        if (!combined.Contains("duplicate")) return false;

        var col = facts.ResolveColumn(description, evidence, column);
        if (col is null) return false;

        // DA ran: it is the authoritative source for duplicate risk
        if (facts.DataAnalysisRan)
            return !facts.HasDuplicateEvidence(col.SnakeCaseName);

        // DA absent + candidate key: must have actual evidence of duplicates
        if (col.IsCandidateKey)
            return !facts.HasDuplicateEvidence(col.SnakeCaseName);

        // Non-candidate-key without DA: contradict only if evidence itself shows unique values
        return EvidenceShowsUniqueValues(col.SnakeCaseName, evidence) &&
               !facts.HasDuplicateEvidence(col.SnakeCaseName);
    }

    private static bool EvidenceShowsUniqueValues(string columnName, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return false;
        var pattern = new Regex(
            $@"\b{Regex.Escape(columnName)}\s*=\s*([^,;\s]+)",
            RegexOptions.IgnoreCase);
        var matches = pattern.Matches(evidence);
        if (matches.Count < 2) return false;
        var values = matches.Select(m => m.Groups[1].Value.ToLowerInvariant()).ToList();
        return values.Count == values.Distinct().Count();
    }
}

/// <summary>
/// Filters primary key recommendations that contradict Data Analysis's authoritative PK choice,
/// and key recommendations for columns DA explicitly marked as not recommended as keys.
/// </summary>
internal sealed class PrimaryKeyRule : IContradictionRule
{
    public bool Contradicts(string description, string? evidence, string? column,
        string levelOrPriority, DeterministicFactSet facts)
    {
        var combined = $"{description} {evidence}".ToLowerInvariant();

        var col = facts.ResolveColumn(description, evidence, column);
        if (col is null) return false;

        // DA says this column is not recommended as a key → filter any key recommendation for it
        if (facts.IsNotRecommendedKey(col.SnakeCaseName) && ClaimSuggestsColumnAsKey(combined))
            return true;

        // DA recommends a specific PK → AI cannot recommend a different column as PK
        if (ClaimSuggestsAsPrimaryKey(combined) && facts.RecommendedPrimaryKey is not null)
            return !facts.RecommendedPrimaryKey.Equals(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static bool ClaimSuggestsColumnAsKey(string combined) =>
        combined.Contains("primary key") ||
        combined.Contains("unique constraint") ||
        (combined.Contains("key") &&
         (combined.Contains("use ") || combined.Contains("designate") || combined.Contains("recommend")));

    private static bool ClaimSuggestsAsPrimaryKey(string combined) =>
        combined.Contains("primary key") &&
        !combined.Contains("no primary key") &&
        !combined.Contains("not recommended") &&
        !combined.Contains("surrogate");
}

// ── Engine ────────────────────────────────────────────────────────────────────

/// <summary>
/// Applies all contradiction rules to filter AI claims that contradict authoritative
/// deterministic findings from Schema, Validation, and Data Analysis.
/// Replaces the phrase-scanning approach of AiReviewClaimValidator with typed,
/// DeterministicFactSet-backed rules.
/// </summary>
internal static class ContradictionEngine
{
    private static readonly IReadOnlyList<IContradictionRule> Rules =
    [
        new TypeStoredAsTextRule(),
        new TypeInferenceRule(),
        new CastToNumericRule(),
        new NullabilityRule(),
        new DuplicateRule(),
        new PrimaryKeyRule(),
    ];

    internal static AiReviewResult Apply(AiReviewResult result, AiReviewRequest request)
    {
        if (request.Mode != AiReviewMode.Dataset) return result;

        var facts = DeterministicFactSet.Build(request);

        var filteredRisks = result.Risks
            .Where(r => !IsContradicted(r.Description, r.Evidence, r.Column, r.Level, facts))
            .ToList();
        var filteredRecs = result.Recommendations
            .Where(r => !IsContradicted(r.Description, r.Evidence, r.Column, r.Priority, facts))
            .ToList();

        if (filteredRisks.Count == result.Risks.Count &&
            filteredRecs.Count  == result.Recommendations.Count)
            return result;

        return new AiReviewResult
        {
            Summary         = result.Summary,
            Risks           = filteredRisks,
            Recommendations = filteredRecs,
        };
    }

    private static bool IsContradicted(
        string description, string? evidence, string? column,
        string levelOrPriority, DeterministicFactSet facts)
        => Rules.Any(rule => rule.Contradicts(description, evidence, column, levelOrPriority, facts));
}
