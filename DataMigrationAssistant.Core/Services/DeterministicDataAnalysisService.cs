using System.Text;
using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class DeterministicDataAnalysisService : IDataAnalysisService
{
    public Task<DataAnalysisResult> AnalyzeAsync(
        DataAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new DataAnalysisResult
        {
            Summary         = BuildSummary(request),
            Findings        = BuildFindings(request),
            Risks           = BuildRisks(request),
            Recommendations = BuildRecommendations(request),
        };
        return Task.FromResult(result);
    }

    // ── Summary ───────────────────────────────────────────────────────────

    internal static string BuildSummary(DataAnalysisRequest request)
    {
        var schema      = request.TableSchema;
        var preview     = request.SheetPreview;
        var entityLabel = schema.TableName.Replace('_', ' ');

        var sb = new StringBuilder();
        sb.Append($"This dataset contains {preview.TotalRowCount} row(s) and {schema.Columns.Count} column(s)");

        if (!string.IsNullOrWhiteSpace(entityLabel))
            sb.Append($", representing {entityLabel}");

        sb.Append('.');

        var structuralCols = schema.Columns.Where(c => c.IsCandidateKey).ToList();
        var strongCols     = schema.Columns.Where(c => c.CandidateKeyQuality == CandidateKeyQuality.Strong).ToList();
        var plausibleCols  = schema.Columns.Where(c => c.CandidateKeyQuality == CandidateKeyQuality.Plausible).ToList();

        if (structuralCols.Count == 0)
        {
            sb.Append(" No unique, non-nullable column was found — a surrogate key is required.");
        }
        else if (structuralCols.Count == 1)
        {
            var col = structuralCols[0];
            if (col.CandidateKeyQuality == CandidateKeyQuality.Strong)
                sb.Append($" Column '{col.SnakeCaseName}' is a strong primary key candidate.");
            else if (col.CandidateKeyQuality == CandidateKeyQuality.Plausible)
                sb.Append($" Column '{col.SnakeCaseName}' is a plausible alternate key.");
            else
                sb.Append($" Column '{col.SnakeCaseName}' is structurally unique but not recommended as a primary key.");
        }
        else
        {
            sb.Append($" {structuralCols.Count} structurally unique columns were detected.");

            if (strongCols.Count == 1)
                sb.Append($" 1 strong primary key candidate: {strongCols[0].SnakeCaseName}.");
            else if (strongCols.Count > 1)
                sb.Append($" {strongCols.Count} strong primary key candidates: {string.Join(", ", strongCols.Select(c => c.SnakeCaseName))}.");

            if (plausibleCols.Count == 1)
                sb.Append($" 1 plausible alternate key: {plausibleCols[0].SnakeCaseName}.");
            else if (plausibleCols.Count > 1)
                sb.Append($" {plausibleCols.Count} plausible alternate keys: {string.Join(", ", plausibleCols.Select(c => c.SnakeCaseName))}.");

            if (strongCols.Count == 0 && plausibleCols.Count == 0)
                sb.Append(" No primary key candidate is recommended — consider adding a surrogate key.");
        }

        var lookupCount = schema.Columns.Count(c => IsLookupCandidate(c.SnakeCaseName));
        if (lookupCount > 0)
            sb.Append($" {lookupCount} column(s) may represent lookup/reference data.");

        return sb.ToString();
    }

    // ── Findings (Key Findings + Normalization Opportunities) ─────────────

    internal static IReadOnlyList<DataAnalysisFinding> BuildFindings(DataAnalysisRequest request)
    {
        var findings = new List<DataAnalysisFinding>();
        var schema   = request.TableSchema;

        // ── Candidate key warning — only emitted when no recommended key exists ──
        // Per-column key quality is now surfaced in the Schema table (Key Quality badge).
        var strongCols    = schema.Columns.Where(c => c.CandidateKeyQuality == CandidateKeyQuality.Strong).ToList();
        var plausibleCols = schema.Columns.Where(c => c.CandidateKeyQuality == CandidateKeyQuality.Plausible).ToList();

        if (strongCols.Count == 0 && plausibleCols.Count == 0)
        {
            var noStructural = schema.Columns.All(c => !c.IsCandidateKey);
            findings.Add(new DataAnalysisFinding
            {
                Category    = "CandidateKey",
                Severity    = "WARNING",
                Description = "No primary key candidate is recommended",
                Detail      = noStructural
                    ? "All columns have either nullable or duplicate values in the sample. Consider adding a surrogate key."
                    : "None of the structurally unique columns are semantically suitable as a primary key. Consider adding a surrogate key.",
            });
        }

        // ── Data quality observations from validation ─────────────────────
        foreach (var w in request.ValidationResult.Warnings)
        {
            findings.Add(new DataAnalysisFinding
            {
                Category    = "DataQuality",
                Severity    = w.Severity == ValidationSeverity.Warning ? "WARNING" : "INFO",
                Description = w.Message,
                Detail      = w.ColumnName is not null ? $"Affects column: {w.ColumnName}" : null,
            });
        }

        // ── Normalization: lookup/reference table candidates ──────────────
        var lookupCandidates = schema.Columns
            .Where(c => IsLookupCandidate(c.SnakeCaseName))
            .ToList();

        foreach (var col in lookupCandidates)
        {
            findings.Add(new DataAnalysisFinding
            {
                Category    = "NormalizationOpportunity",
                Severity    = "INFO",
                Description = $"'{col.SnakeCaseName}' is a lookup/reference table candidate",
                Detail      = "Columns with names ending in '_type', '_status', '_category', or '_group' often represent finite enumerable values that belong in a separate reference table.",
            });
        }

        // ── Normalization: column groups with a shared prefix ─────────────
        foreach (var (prefix, cols) in DetectPrefixGroups(schema.Columns))
        {
            findings.Add(new DataAnalysisFinding
            {
                Category    = "NormalizationOpportunity",
                Severity    = "INFO",
                Description = $"Column group with prefix '{prefix}': {string.Join(", ", cols.Select(c => c.SnakeCaseName))}",
                Detail      = "Columns sharing a common prefix may describe a separate logical entity suitable for extraction into its own table.",
            });
        }

        // ── Normalization: columns that look like FKs ─────────────────────
        var fkLikeCols = schema.Columns
            .Where(c => !c.IsCandidateKey
                     && c.SnakeCaseName.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.SnakeCaseName, "id", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var col in fkLikeCols)
        {
            findings.Add(new DataAnalysisFinding
            {
                Category    = "NormalizationOpportunity",
                Severity    = "INFO",
                Description = $"'{col.SnakeCaseName}' appears to be a foreign key reference",
                Detail      = "Columns ending in '_id' that are not the primary key typically reference another table. Verify whether the referenced table exists or should be created.",
            });
        }

        return findings;
    }

    // ── Risks (Duplicate + Nullable) ──────────────────────────────────────

    internal static IReadOnlyList<DataAnalysisFinding> BuildRisks(DataAnalysisRequest request)
    {
        var risks  = new List<DataAnalysisFinding>();
        var schema = request.TableSchema;

        // Duplicate risks — skip categorical columns where duplicates are expected by design
        foreach (var col in schema.Columns.Where(c =>
            c.HasDuplicates &&
            !c.IsCandidateKey &&
            !IsCategoricalColumn(c)))
        {
            risks.Add(new DataAnalysisFinding
            {
                Category    = "DuplicateRisk",
                Severity    = "WARNING",
                Description = $"'{col.SnakeCaseName}' contains duplicate values",
                Detail      = $"Type: {col.InferredType}. If uniqueness is required, data cleaning or a UNIQUE constraint analysis is recommended.",
            });
        }

        // Nullable risks on columns that look important
        foreach (var col in schema.Columns.Where(c => c.IsNullable && IsImportantColumn(c.SnakeCaseName)))
        {
            risks.Add(new DataAnalysisFinding
            {
                Category    = "NullableRisk",
                Severity    = "WARNING",
                Description = $"'{col.SnakeCaseName}' is nullable but appears to be an important field",
                Detail      = "Columns with names suggesting identity or key relationships (ending in '_id', '_code', '_key', or named 'id', 'name', 'code') are typically NOT NULL. Verify whether nulls are intentional.",
            });
        }

        return risks;
    }

    // ── Recommendations ───────────────────────────────────────────────────

    internal static IReadOnlyList<DataAnalysisRecommendation> BuildRecommendations(DataAnalysisRequest request)
    {
        var recs   = new List<DataAnalysisRecommendation>();
        var schema = request.TableSchema;

        // Semantically qualified candidates: Plausible or Strong, ranked by quality then column order
        var qualifiedCandidates = schema.Columns
            .Where(c => c.CandidateKeyQuality >= CandidateKeyQuality.Plausible)
            .OrderByDescending(c => (int)c.CandidateKeyQuality)
            .ThenBy(c => c.Index)
            .ToList();

        // Primary key recommendation
        if (qualifiedCandidates.Count >= 1)
        {
            var pk = qualifiedCandidates[0];
            var pkDetail = qualifiedCandidates.Count == 1
                ? $"Designate '{pk.SnakeCaseName}' as the PRIMARY KEY — it is the only non-nullable column with all unique values."
                : $"Designate '{pk.SnakeCaseName}' as the PRIMARY KEY — it is the strongest candidate key by type and naming convention.";
            recs.Add(new DataAnalysisRecommendation
            {
                Priority    = "HIGH",
                Type        = "PrimaryKey",
                Description = pkDetail,
            });
        }
        else
        {
            recs.Add(new DataAnalysisRecommendation
            {
                Priority    = "HIGH",
                Type        = "PrimaryKey",
                Description = "Add a surrogate primary key (e.g., id SERIAL PRIMARY KEY) — no natural unique identifier was found in the sample.",
            });
        }

        // Optional UNIQUE constraints for remaining qualified candidates (e.g., business keys)
        foreach (var ck in qualifiedCandidates.Skip(1))
        {
            recs.Add(new DataAnalysisRecommendation
            {
                Priority    = "MEDIUM",
                Type        = "UniqueConstraint",
                Description = $"Add UNIQUE constraint on '{ck.SnakeCaseName}' — it has all unique values and may serve as an alternate key.",
            });
        }

        // Index suggestions on FK-like columns
        var fkLikeCols = schema.Columns
            .Where(c => !c.IsCandidateKey
                     && c.SnakeCaseName.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.SnakeCaseName, "id", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var col in fkLikeCols)
        {
            recs.Add(new DataAnalysisRecommendation
            {
                Priority    = "MEDIUM",
                Type        = "Index",
                Description = $"Add an index on '{col.SnakeCaseName}' — its naming convention suggests it is used in JOIN or filter operations.",
            });
        }

        // Normalization recommendations
        var lookupCandidates = schema.Columns
            .Where(c => IsLookupCandidate(c.SnakeCaseName))
            .ToList();

        if (lookupCandidates.Count > 0)
        {
            var colList = string.Join(", ", lookupCandidates.Select(c => $"'{c.SnakeCaseName}'"));
            recs.Add(new DataAnalysisRecommendation
            {
                Priority    = "LOW",
                Type        = "Normalization",
                Description = $"Consider extracting {lookupCandidates.Count} column(s) into lookup/reference tables: {colList}.",
            });
        }

        foreach (var (prefix, cols) in DetectPrefixGroups(schema.Columns))
        {
            recs.Add(new DataAnalysisRecommendation
            {
                Priority    = "LOW",
                Type        = "Normalization",
                Description = $"Column group '{prefix}_*' ({cols.Count} columns) may represent a separate entity — consider extracting into its own table.",
            });
        }

        return recs;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    internal static string GetTypeDisqualificationReason(PostgresType type) => type switch
    {
        PostgresType.Boolean   => "it can only hold two distinct values",
        PostgresType.Numeric   => "it is a numeric value column",
        PostgresType.Date      => "it is a date column",
        PostgresType.Timestamp => "it is a timestamp column",
        _                      => "its type is not suitable as a database key",
    };

    // Categorical columns by type or name — duplicates are expected by design
    internal static bool IsCategoricalColumn(ColumnSchema col) =>
        col.InferredType == PostgresType.Boolean ||
        IsLookupCandidate(col.SnakeCaseName) ||
        col.SnakeCaseName.EndsWith("_flag", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLookupCandidate(string columnName) =>
        columnName.EndsWith("_type",     StringComparison.OrdinalIgnoreCase) ||
        columnName.EndsWith("_status",   StringComparison.OrdinalIgnoreCase) ||
        columnName.EndsWith("_category", StringComparison.OrdinalIgnoreCase) ||
        columnName.EndsWith("_group",    StringComparison.OrdinalIgnoreCase);

    internal static bool IsImportantColumn(string columnName) =>
        string.Equals(columnName, "id",   StringComparison.OrdinalIgnoreCase) ||
        string.Equals(columnName, "name", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(columnName, "code", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(columnName, "key",  StringComparison.OrdinalIgnoreCase) ||
        columnName.EndsWith("_id",   StringComparison.OrdinalIgnoreCase) ||
        columnName.EndsWith("_code", StringComparison.OrdinalIgnoreCase) ||
        columnName.EndsWith("_key",  StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<(string Prefix, IReadOnlyList<ColumnSchema> Columns)>
        DetectPrefixGroups(IReadOnlyList<ColumnSchema> columns)
    {
        var prefixMap = new Dictionary<string, List<ColumnSchema>>(StringComparer.OrdinalIgnoreCase);

        foreach (var col in columns)
        {
            var underscore = col.SnakeCaseName.IndexOf('_', StringComparison.Ordinal);
            if (underscore <= 0) continue;

            var prefix = col.SnakeCaseName[..underscore];
            if (!prefixMap.TryGetValue(prefix, out var list))
                prefixMap[prefix] = list = [];
            list.Add(col);
        }

        return prefixMap
            .Where(kv => kv.Value.Count >= 3)
            .Select(kv => (kv.Key, (IReadOnlyList<ColumnSchema>)kv.Value))
            .ToList();
    }
}
