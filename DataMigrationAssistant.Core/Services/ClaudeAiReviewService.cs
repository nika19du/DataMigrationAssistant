using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using DataMigrationAssistant.Core.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DataMigrationAssistant.Core.Services;

public sealed class ClaudeAiReviewService : IAiReviewService
{
    private const string ModelId = "claude-sonnet-4-6";

    private readonly string? _apiKey;

    public ClaudeAiReviewService()
        => _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    internal ClaudeAiReviewService(string? apiKey) => _apiKey = apiKey;

    public async Task<AiReviewResult> ReviewAsync(AiReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");

        var client = new AnthropicClient(new ClientOptions { ApiKey = _apiKey });
        var userMessage = AiReviewPromptBuilder.BuildUserMessage(request);

        var parameters = new MessageCreateParams
        {
            Model    = ModelId,
            MaxTokens = 2048,
            System   = AiReviewPromptBuilder.GetSystemPrompt(request.Mode),
            Messages = [new MessageParam { Role = Role.User, Content = userMessage }],
        };

        var response = await client.Messages.Create(parameters, cancellationToken);
        var json = ExtractText(response);
        var raw              = AiReviewResponseParser.Parse(json);
        var evidenceFiltered = AiReviewEvidenceFilter.Apply(raw, request.Mode);
        var claimValidated   = ContradictionEngine.Apply(evidenceFiltered, request);
        return AiReviewGroundedFallback.Apply(claimValidated, request);
    }

    private static string ExtractText(Message response)
    {
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
                return textBlock.Text;
        }
        return string.Empty;
    }
}

internal static class AiReviewPromptBuilder
{
    internal const int MaxSampleRows = 20;

    internal const string SystemPrompt =
        "You are a database migration reviewer. " +
        "Before making recommendations, inspect the <migration_sql> block carefully: " +
        "do not suggest adding SQL clauses that are already present in it, " +
        "and if ON CONFLICT DO NOTHING already appears in the SQL, do not recommend adding it. " +
        "Lines starting with -- are SQL comments and must not be treated as executable SQL. " +
        "Removed rows appear as commented-out lines in the SQL; they do not delete data from the database. " +
        "When writing Action values, use concrete safe next steps such as: " +
        "'Run migration in staging first', 'Wrap migration in a transaction', " +
        "'Review removed-row comments manually', 'Check row counts before and after', " +
        "'Verify UPDATE WHERE clauses target the correct rows'. " +
        "Do not use a random SQL statement as an Action value. " +
        "Respond ONLY with valid JSON matching this schema: " +
        "{\"summary\":string,\"risks\":[{\"level\":\"HIGH|MEDIUM|LOW\",\"description\":string}]," +
        "\"recommendations\":[{\"priority\":\"HIGH|MEDIUM|LOW\",\"description\":string,\"action\":string|null}]}. " +
        "Do not include any text outside the JSON object.";

    internal const string DatasetSystemPrompt =
        "You are a data quality analyst and explanation layer for deterministic findings. " +
        "The Schema, Validation results, and Data Analysis results provided are authoritative — " +
        "do not re-derive or contradict them. " +
        "Schema inference is authoritative for column types and nullability. " +
        "Validation is authoritative for key selection and data warnings. " +
        "Data Analysis is authoritative for key quality, duplicate risk, and nullable risk. " +
        "AI Review may explain deterministic findings. " +
        "AI Review may elaborate on recommendations with concrete next steps. " +
        "AI Review may not contradict any authoritative finding. " +
        "Do not independently analyze the dataset to produce risks already covered by the deterministic layers. " +
        "Only report risks and recommendations that are directly supported by the provided schema, validation warnings, or sample rows. " +
        "If evidence is not present in context, do not report the issue. " +
        "Absence of evidence is not evidence of risk. " +
        "Every risk must cite its evidence from the schema, validation warnings, or sample rows. " +
        "Analyze the provided schema, sample rows, and validation warnings to identify: " +
        "likely primary keys and candidate keys, duplicate risks, nullable column risks, " +
        "type inference risks (e.g. numbers stored as text, ambiguous dates), " +
        "decimal and culture-sensitive formatting issues, normalization opportunities, " +
        "and SQL generation recommendations such as appropriate PostgreSQL column types or constraints. " +
        "Do not mention migration SQL, UPDATE statements, diff summaries, or staging environments " +
        "unless the dataset context clearly calls for it. " +
        "Factual accuracy rules: " +
        "Do not claim a column stores values as text if the schema infers NUMERIC or BOOLEAN for that column. " +
        "Do not claim comma decimal issues unless sample rows or validation warnings explicitly contain comma-formatted numbers. " +
        "Do not claim duplicate risk unless duplicate values are explicitly present in sample rows or validation warnings. " +
        "Do not claim nullability risk unless the schema marks the column nullable or validation warnings report missing values. " +
        "Type inference rules: " +
        "If the schema already inferred BOOLEAN for a column, do not recommend boolean conversion — the type is already correct. " +
        "If the schema already inferred NUMERIC for a column and no decimal or formatting warning exists in validation_warnings, do not recommend decimal normalization. " +
        "If the schema already inferred NUMERIC for a column, do not report incorrect type inference — the inference succeeded. " +
        "Different decimal separators such as comma versus dot indicate source formatting differences only; they do not imply incorrect type inference. " +
        "Only report type inference risk when the schema inferred TEXT and sample values clearly indicate a different type such as a number or date. " +
        "Duplicate risk rules: " +
        "Candidate key columns should not be reported as duplicate risks unless duplicate values are explicitly detected in the sample rows or reported in validation_warnings. " +
        "When writing Action values, use concrete data-cleaning steps such as: " +
        "'Standardize date format to ISO 8601', 'Remove leading/trailing whitespace from column X', " +
        "'Deduplicate rows by column Y', 'Confirm nullability intent for column Z', " +
        "'Cast column W to numeric before inserting'. " +
        "Do not use a random SQL statement as an Action value. " +
        "Candidate key interpretation rules: " +
        "When the schema lists multiple columns marked as candidate key, they are separate columns that each happen to be unique in the sample — they are NOT a composite key. " +
        "Do not describe them as a combined or composite key unless the context explicitly says composite key. " +
        "Do not recommend a UNIQUE constraint that combines all candidate key columns together (e.g. UNIQUE(id, username, score)). " +
        "The first listed candidate key column is the system-selected recommended primary key; prefer recommending PRIMARY KEY on that column. " +
        "Business identifier columns such as username or email may warrant an individual UNIQUE constraint, but are generally poor primary keys because they are mutable and may collide at scale. " +
        "Value or measurement columns such as score, amount, or rate should not be recommended as keys or UNIQUE constraints solely because they appear unique in the sample — uniqueness in a small sample does not hold at production scale. " +
        "Claim-evidence validation rules: " +
        "Nullable risk may only be reported when: the schema marks the column as nullable (NULL), " +
        "at least one sample row contains NULL for that column, or a validation warning explicitly mentions missing values for that column. " +
        "A non-null value such as FALSE or 0 is not evidence of nullability. " +
        "Boolean conversion risk may only be reported when: the schema shows TEXT as the inferred type " +
        "AND sample values contain boolean-like patterns (TRUE/FALSE, Yes/No, Y/N). " +
        "If the schema already infers BOOLEAN for a column, do not report boolean conversion risk for that column. " +
        "Numeric formatting risk may only be reported when: sample rows contain values with culture-specific decimal separators " +
        "AND schema inference or validation warnings indicate type ambiguity. " +
        "The presence of a comma in a value alone does not prove the column is stored as text. " +
        "Do not claim text storage unless the schema explicitly shows TEXT as the inferred type. " +
        "Duplicate risk may only be reported when: duplicate values are explicitly visible in sample rows " +
        "OR validation warnings explicitly mention duplicates for that column. " +
        "Candidate key columns must not be reported as duplicate risks unless duplicates are explicitly detected " +
        "in sample rows or validation warnings. " +
        "Primary key risk may only be reported when: multiple candidate key columns exist in the schema " +
        "OR validation warnings discuss key ambiguity. " +
        "Evidence must logically support the claim. Having evidence is not sufficient — " +
        "the evidence must directly justify the reported risk, not merely be associated with the column. " +
        "Data Analysis authority rules: " +
        "The <data_analysis_authority> section, when present, contains deterministic findings computed from the full dataset before AI Review ran. " +
        "Data Analysis is the authoritative source. Its findings override AI interpretation of sample rows. " +
        "If Data Analysis says a column is a numeric value column, do not raise type inference risk for that column. " +
        "If Data Analysis says a column is not recommended as a key, do not recommend it as a primary key or unique constraint. " +
        "If Data Analysis recommends a specific column as the primary key, do not recommend a different column as primary key. " +
        "If Data Analysis found no duplicate risk for a column, do not report duplicate risk for that column. " +
        "If Data Analysis found no nullability issue for a column marked NOT NULL, do not report nullability risk for that column. " +
        "When sample-row interpretation conflicts with Data Analysis findings, prefer Data Analysis. " +
        "AI Review may explain, summarize, or elaborate on Data Analysis findings but may not escalate a deterministic finding into a higher-severity risk. " +
        "AI Review may only introduce a new risk if: (1) it is not addressed by Data Analysis AND (2) it has direct evidence from schema, validation warnings, or sample rows. " +
        "Respond ONLY with valid JSON matching this schema: " +
        "{\"summary\":string,\"risks\":[{\"level\":\"HIGH|MEDIUM|LOW\",\"description\":string,\"evidence\":string,\"column\":string|null}]," +
        "\"recommendations\":[{\"priority\":\"HIGH|MEDIUM|LOW\",\"description\":string,\"action\":string|null,\"evidence\":string,\"column\":string|null}]}. " +
        "The column field must name the single column the risk or recommendation applies to, or null if it applies to the whole dataset. " +
        "The evidence field must quote the exact text from schema, validation warnings, or sample rows that supports each item. " +
        "If no context evidence exists for an item, omit that item entirely. " +
        "Do not include any text outside the JSON object.";

    internal static string GetSystemPrompt(AiReviewMode mode) =>
        mode == AiReviewMode.Dataset ? DatasetSystemPrompt : SystemPrompt;

    internal static string BuildUserMessage(AiReviewRequest request) =>
        request.Mode == AiReviewMode.Dataset
            ? BuildDatasetUserMessage(request)
            : BuildMigrationUserMessage(request);

    private static string BuildMigrationUserMessage(AiReviewRequest request)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Inspect the <migration_sql> section below before writing recommendations.");
        sb.AppendLine("Do not suggest adding any SQL clause that already appears in it.");
        sb.AppendLine();

        AppendSchema(sb, request);
        AppendValidationWarnings(sb, request);

        if (request.SeedDiffResult is { } diff)
        {
            var added     = diff.Rows.Count(r => r.Status == SeedDiffStatus.Added);
            var removed   = diff.Rows.Count(r => r.Status == SeedDiffStatus.Removed);
            var changed   = diff.Rows.Count(r => r.Status == SeedDiffStatus.Changed);
            var unchanged = diff.Rows.Count(r => r.Status == SeedDiffStatus.Unchanged);

            sb.AppendLine("<diff_summary>");
            sb.AppendLine($"  Added rows    : {added}");
            sb.AppendLine($"  Removed rows  : {removed}");
            sb.AppendLine($"  Changed rows  : {changed}");
            sb.AppendLine($"  Unchanged rows: {unchanged}");
            sb.AppendLine("</diff_summary>");
            sb.AppendLine();
        }

        AppendSampleRows(sb, request);

        if (!string.IsNullOrWhiteSpace(request.MigrationSql))
        {
            sb.AppendLine("<migration_sql>");
            sb.AppendLine(request.MigrationSql);
            sb.AppendLine("</migration_sql>");
        }

        return sb.ToString();
    }

    private static string BuildDatasetUserMessage(AiReviewRequest request)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Analyze the following dataset for data quality issues.");
        sb.AppendLine("Focus on primary/candidate keys, duplicate risks, nullable risks,");
        sb.AppendLine("type inference accuracy, decimal/culture formatting, normalization,");
        sb.AppendLine("and SQL generation recommendations.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.SheetPreview.SheetName))
        {
            sb.AppendLine($"<source>");
            sb.AppendLine($"  Sheet: {request.SheetPreview.SheetName}");
            sb.AppendLine($"  File : {request.SheetPreview.FilePath}");
            sb.AppendLine($"  Total rows: {request.SheetPreview.TotalRowCount}");
            sb.AppendLine($"</source>");
            sb.AppendLine();
        }

        AppendSchema(sb, request);
        AppendValidationWarnings(sb, request);
        AppendSampleRows(sb, request);
        AppendDataAnalysisAuthority(sb, request);

        return sb.ToString();
    }

    private static void AppendSchema(StringBuilder sb, AiReviewRequest request)
    {
        sb.AppendLine("<schema>");
        sb.AppendLine($"  Table: {request.TableSchema.TableName}");
        sb.AppendLine("  Columns:");
        foreach (var col in request.TableSchema.Columns)
        {
            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            var key = col.IsCandidateKey ? ", candidate key" : string.Empty;
            sb.AppendLine($"  - {col.SnakeCaseName} ({col.InferredType}, {nullable}{key})");
        }
        sb.AppendLine("</schema>");
        sb.AppendLine();
    }

    private static void AppendValidationWarnings(StringBuilder sb, AiReviewRequest request)
    {
        sb.AppendLine("<validation_warnings>");
        if (request.ValidationResult.HasWarnings)
        {
            foreach (var w in request.ValidationResult.Warnings)
                sb.AppendLine($"  [{w.Severity.ToString().ToUpperInvariant()}] {w.Message}");
        }
        else
        {
            sb.AppendLine("  (none)");
        }
        sb.AppendLine("</validation_warnings>");
        sb.AppendLine();
    }

    private static void AppendSampleRows(StringBuilder sb, AiReviewRequest request)
    {
        var sampleRows = request.SheetPreview.Rows.Take(MaxSampleRows).ToList();
        sb.AppendLine($"<sample_rows count=\"{sampleRows.Count}\">");
        for (int i = 0; i < sampleRows.Count; i++)
        {
            var row = sampleRows[i];
            var cells = string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}"));
            sb.AppendLine($"  Row {i + 1}: {cells}");
        }
        sb.AppendLine("</sample_rows>");
        sb.AppendLine();
    }

    private static void AppendDataAnalysisAuthority(StringBuilder sb, AiReviewRequest request)
    {
        var analysis = request.DataAnalysisResult;
        if (analysis is null || string.IsNullOrWhiteSpace(analysis.Summary))
            return;

        sb.AppendLine("<data_analysis_authority>");
        sb.AppendLine("  Note: these findings are DETERMINISTIC and AUTHORITATIVE. Do not contradict them.");
        sb.AppendLine($"  Summary: {analysis.Summary}");

        if (analysis.Findings.Count > 0)
        {
            sb.AppendLine("  Findings:");
            foreach (var f in analysis.Findings)
            {
                var detail = f.Detail is not null ? $" Detail: {f.Detail}" : string.Empty;
                sb.AppendLine($"  - [{f.Severity}] {f.Category}: {f.Description}.{detail}");
            }
        }
        else
        {
            sb.AppendLine("  Findings: (none)");
        }

        if (analysis.Risks.Count > 0)
        {
            sb.AppendLine("  Risks:");
            foreach (var r in analysis.Risks)
            {
                var detail = r.Detail is not null ? $" Detail: {r.Detail}" : string.Empty;
                sb.AppendLine($"  - [{r.Severity}] {r.Category}: {r.Description}.{detail}");
            }
        }
        else
        {
            sb.AppendLine("  Risks: (none — Data Analysis found no duplicate or nullability risks)");
        }

        if (analysis.Recommendations.Count > 0)
        {
            sb.AppendLine("  Recommendations:");
            foreach (var r in analysis.Recommendations)
                sb.AppendLine($"  - [{r.Priority}] {r.Type}: {r.Description}");
        }
        else
        {
            sb.AppendLine("  Recommendations: (none)");
        }

        sb.AppendLine("</data_analysis_authority>");
        sb.AppendLine();
    }
}

internal static class AiReviewResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static AiReviewResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new AiReviewResult { Summary = "No response from AI." };

        try
        {
            var dto = JsonSerializer.Deserialize<AiReviewResultDto>(json, Options);
            if (dto is null) return new AiReviewResult { Summary = "Could not parse AI response." };

            return new AiReviewResult
            {
                Summary = dto.Summary ?? string.Empty,
                Risks = (dto.Risks ?? [])
                    .Select(r => new AiReviewRisk
                    {
                        Level       = r.Level ?? string.Empty,
                        Description = r.Description ?? string.Empty,
                        Evidence    = r.Evidence,
                        Column      = r.Column,
                    })
                    .ToList(),
                Recommendations = (dto.Recommendations ?? [])
                    .Select(r => new AiReviewRecommendation
                    {
                        Priority    = r.Priority ?? string.Empty,
                        Description = r.Description ?? string.Empty,
                        Action      = r.Action,
                        Evidence    = r.Evidence,
                        Column      = r.Column,
                    })
                    .ToList(),
            };
        }
        catch (JsonException)
        {
            return new AiReviewResult { Summary = "Could not parse AI response." };
        }
    }

    private sealed class AiReviewResultDto
    {
        [JsonPropertyName("summary")]         public string? Summary { get; init; }
        [JsonPropertyName("risks")]           public List<AiReviewRiskDto>? Risks { get; init; }
        [JsonPropertyName("recommendations")] public List<AiReviewRecommendationDto>? Recommendations { get; init; }
    }

    private sealed class AiReviewRiskDto
    {
        [JsonPropertyName("level")]       public string? Level { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("evidence")]    public string? Evidence { get; init; }
        [JsonPropertyName("column")]      public string? Column { get; init; }
    }

    private sealed class AiReviewRecommendationDto
    {
        [JsonPropertyName("priority")]    public string? Priority { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("action")]      public string? Action { get; init; }
        [JsonPropertyName("evidence")]    public string? Evidence { get; init; }
        [JsonPropertyName("column")]      public string? Column { get; init; }
    }
}

internal static class AiReviewEvidenceFilter
{
    private static readonly string[] ContextSourceWords =
        ["schema", "validation", "analysis", "sample", "preview", "row", "warning"];

    internal static AiReviewResult Apply(AiReviewResult result, AiReviewMode mode)
    {
        if (mode != AiReviewMode.Dataset) return result;

        var filteredRisks = result.Risks.Where(r => HasContextEvidence(r.Evidence)).ToList();
        var filteredRecs  = result.Recommendations.Where(r => HasContextEvidence(r.Evidence)).ToList();

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

    private static bool HasContextEvidence(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return false;
        var lower = evidence.ToLowerInvariant();
        return ContextSourceWords.Any(w => lower.Contains(w));
    }
}

internal enum RiskCategory
{
    TypeInference,
    Nullability,
    Duplicate,
    PrimaryKey,
    Formatting,
    Other,
}

internal static class AiReviewClaimValidator
{
    private static readonly string[] TypeInferencePhrases =
    [
        "incorrect type inference",
        "inconsistent data types",
        "type mismatch",
        "stored as text",
        "text storage",
        "non-numeric",
        "wrong type",
        "inferred incorrectly",
        "type conversion issue",
        "incorrect type",
        "data type inconsistency",
        "boolean conversion",
    ];

    private static readonly string[] FormattingPhrases =
    [
        "culture-specific decimal separator",
        "comma decimal",
        "locale formatting",
        "decimal formatting",
        "comma versus dot",
    ];

    /// <summary>
    /// Delegates to ContradictionEngine.Apply, which applies all typed contradiction rules
    /// backed by DeterministicFactSet. Kept for backward compatibility with existing callers.
    /// </summary>
    internal static AiReviewResult Apply(AiReviewResult result, AiReviewRequest request)
        => ContradictionEngine.Apply(result, request);

    internal static RiskCategory ClassifyClaim(string description, string? evidence)
    {
        var combined = $"{description} {evidence}".ToLowerInvariant();

        if (TypeInferencePhrases.Any(p => combined.Contains(p)))
            return RiskCategory.TypeInference;

        if (combined.Contains("null") && !combined.Contains("not null") && !combined.Contains("nonnull"))
            return RiskCategory.Nullability;

        if (combined.Contains("duplicate"))
            return RiskCategory.Duplicate;

        if (FormattingPhrases.Any(p => combined.Contains(p)))
            return RiskCategory.Formatting;

        return RiskCategory.Other;
    }

    private static bool IsClaimValid(string description, string? evidence, string level, AiReviewRequest request)
    {
        var combined = $"{description} {evidence}".ToLowerInvariant();
        var col      = FindReferencedColumn(combined, request.TableSchema.Columns);
        var category = ClassifyClaim(description, evidence);

        if (EvidenceContradictsClaim(description, evidence, col, request.ValidationResult.Warnings))
            return false;

        if (IsContradictsDataAnalysis(combined, level, request.DataAnalysisResult, request.TableSchema.Columns))
            return false;

        return category switch
        {
            RiskCategory.TypeInference => IsTypeInferenceClaimValid(col),
            RiskCategory.Nullability   => NullabilityEvidenceExists(col, request),
            RiskCategory.Duplicate     => IsDuplicateClaimValid(col, request),
            RiskCategory.Formatting    => IsFormattingClaimValid(col, level),
            _                          => true,
        };
    }

    internal static bool IsContradictsDataAnalysis(
        string combined,
        string levelOrPriority,
        DataAnalysisResult? analysis,
        IReadOnlyList<ColumnSchema> columns)
    {
        if (analysis is null || string.IsNullOrWhiteSpace(analysis.Summary))
            return false;

        // Use word-boundary matching so short names like "id" don't match inside "candidate".
        var col = FindColumnWithWordBoundary(combined, columns);

        if (col is not null)
        {
            // Rule 1: DA marks column "not recommended as a key"
            //         → filter AI claims recommending it as PK or UNIQUE constraint
            var isNotRecommendedAsKey = analysis.Findings.Any(f =>
                f.Category == "CandidateKey" &&
                f.Description.Contains("not recommended as a key", StringComparison.OrdinalIgnoreCase) &&
                f.Description.Contains(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase));

            if (isNotRecommendedAsKey && ClaimSuggestsColumnAsKey(combined))
                return true;

            // Rule 2: DA says column is a numeric value column
            //         → filter HIGH/MEDIUM type inference or format ambiguity claims about it
            if (IsHighOrMedium(levelOrPriority))
            {
                var isNumericValueColumn = analysis.Findings.Any(f =>
                    f.Category == "CandidateKey" &&
                    f.Description.Contains(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase) &&
                    f.Detail != null &&
                    f.Detail.Contains("numeric value column", StringComparison.OrdinalIgnoreCase));

                if (isNumericValueColumn && ClaimSuggestsTypeOrFormatIssue(combined))
                    return true;
            }

            // Rule 3: DA ran and found no duplicate risk for this column
            //         → filter AI duplicate claim (DA is authoritative)
            if (combined.Contains("duplicate"))
            {
                var hasDataAnalysisDuplicateRisk = analysis.Risks.Any(f =>
                    f.Category == "DuplicateRisk" &&
                    f.Description.Contains(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase));
                if (!hasDataAnalysisDuplicateRisk)
                    return true;
            }

            // Rule 4: DA ran and found no nullability issue for a NOT NULL column
            //         → filter AI nullability claim (DA is authoritative)
            if (!col.IsNullable && ClaimMentionsNullability(combined))
            {
                var hasDataAnalysisNullableRisk = analysis.Risks.Any(f =>
                    f.Category == "NullableRisk" &&
                    f.Description.Contains(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase));
                if (!hasDataAnalysisNullableRisk)
                    return true;
            }

            // Rule 5: DA recommends a specific PK, but AI is recommending this different column as PK
            if (ClaimSuggestsAsPrimaryKey(combined))
            {
                var pkRec = analysis.Recommendations.FirstOrDefault(r =>
                    r.Type == "PrimaryKey" &&
                    !r.Description.Contains("surrogate", StringComparison.OrdinalIgnoreCase));
                if (pkRec is not null &&
                    !pkRec.Description.Contains(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    // Word-boundary aware column finder: avoids short names like "id" matching inside "candidate".
    private static ColumnSchema? FindColumnWithWordBoundary(string combined, IReadOnlyList<ColumnSchema> columns)
    {
        foreach (var col in columns)
        {
            var name = col.SnakeCaseName.ToLowerInvariant();
            if (!combined.Contains(name)) continue;

            var idx = 0;
            while ((idx = combined.IndexOf(name, idx, StringComparison.Ordinal)) >= 0)
            {
                var beforeOk = idx == 0 || !char.IsLetterOrDigit(combined[idx - 1]);
                var afterOk  = (idx + name.Length) >= combined.Length || !char.IsLetterOrDigit(combined[idx + name.Length]);
                if (beforeOk && afterOk) return col;
                idx++;
            }
        }
        return null;
    }

    private static bool ClaimSuggestsColumnAsKey(string combined) =>
        combined.Contains("primary key") ||
        combined.Contains("unique constraint") ||
        (combined.Contains("key") && (combined.Contains("use ") || combined.Contains("designate") || combined.Contains("recommend")));

    private static bool ClaimSuggestsAsPrimaryKey(string combined) =>
        combined.Contains("primary key") &&
        !combined.Contains("no primary key") &&
        !combined.Contains("not recommended") &&
        !combined.Contains("surrogate");

    private static bool ClaimSuggestsTypeOrFormatIssue(string combined) =>
        combined.Contains("type inference") ||
        combined.Contains("type risk") ||
        combined.Contains("ambiguous numeric") ||
        combined.Contains("ambiguous format") ||
        combined.Contains("incorrect type") ||
        combined.Contains("type mismatch") ||
        combined.Contains("type conversion") ||
        combined.Contains("culture-specific");

    // Formatting claims about NUMERIC columns are only allowed at LOW severity.
    // HIGH/MEDIUM formatting claims (e.g. "comma separator is a HIGH risk") are filtered.
    private static bool IsFormattingClaimValid(ColumnSchema? col, string level)
    {
        if (col?.InferredType != PostgresType.Numeric) return true;
        return !IsHighOrMedium(level);
    }

    private static bool IsHighOrMedium(string level) =>
        level.Equals("HIGH", StringComparison.OrdinalIgnoreCase) ||
        level.Equals("MEDIUM", StringComparison.OrdinalIgnoreCase);

    // Returns true when the claim's own evidence field directly contradicts what the claim asserts.
    internal static bool EvidenceContradictsClaim(
        string description,
        string? evidence,
        ColumnSchema? col,
        IReadOnlyList<ValidationWarning> warnings)
    {
        if (col is null) return false;

        var descLower = description.ToLowerInvariant();

        // Nullability: claim says missing/no value but evidence shows all explicit non-null values
        if (ClaimMentionsNullability(descLower) && EvidenceShowsOnlyNonNullValues(col, evidence))
            return true;

        // Boolean: claim suggests converting to boolean but schema already inferred BOOLEAN
        if (ClaimSuggestsBooleanConversion(descLower) && col.InferredType == PostgresType.Boolean)
            return true;

        // Type: claim says wrong type / stored-as-text but schema correctly inferred NUMERIC or BOOLEAN
        if (ClaimSaysWrongType(descLower) &&
            (col.InferredType == PostgresType.Numeric || col.InferredType == PostgresType.Boolean))
            return true;

        // Duplicate: claim says duplicate but evidence explicitly shows unique values and there is no duplicate warning
        if (ClaimSaysDuplicate(descLower) &&
            EvidenceShowsUniqueValues(col, evidence) &&
            !HasDuplicateWarningForColumn(col, warnings))
            return true;

        // Cast/convert to numeric: column is already NUMERIC so the cast is unnecessary
        if (ClaimSuggestsCastToNumeric(descLower) && col.InferredType == PostgresType.Numeric)
            return true;

        return false;
    }

    private static bool ClaimMentionsNullability(string descLower) =>
        descLower.Contains("missing value") ||
        descLower.Contains("nullability") ||
        descLower.Contains("nullable concern") ||
        descLower.Contains("does not contain");

    private static bool ClaimSuggestsBooleanConversion(string descLower) =>
        descLower.Contains("boolean conversion") ||
        (descLower.Contains("boolean") && descLower.Contains("convert"));

    private static bool ClaimSaysWrongType(string descLower) =>
        descLower.Contains("type mismatch") ||
        descLower.Contains("wrong type") ||
        descLower.Contains("incorrect type") ||
        descLower.Contains("stored as text") ||
        descLower.Contains("text storage") ||
        descLower.Contains("non-numeric");

    // Detects recommendations like "Cast score to numeric before inserting" that are unnecessary
    // when the schema already inferred NUMERIC for the column.
    private static bool ClaimSuggestsCastToNumeric(string descLower) =>
        (descLower.Contains("cast") || descLower.Contains("convert")) &&
        descLower.Contains("to numeric");

    private static bool ClaimSaysDuplicate(string descLower) =>
        descLower.Contains("duplicate");

    // Returns true when every col=VALUE match in the evidence text is a non-null value.
    // Returns false when there are no matches or any match is NULL.
    private static bool EvidenceShowsOnlyNonNullValues(ColumnSchema col, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return false;

        var pattern = new Regex(
            $@"\b{Regex.Escape(col.SnakeCaseName)}\s*=\s*([^,;\s]+)",
            RegexOptions.IgnoreCase);
        var matches = pattern.Matches(evidence);

        if (matches.Count == 0) return false;

        return matches.All(m =>
            !m.Groups[1].Value.Equals("NULL", StringComparison.OrdinalIgnoreCase));
    }

    // Returns true when the evidence text contains 2+ col=VALUE matches that are all distinct.
    // Returns false when there are fewer than 2 matches (not enough data to conclude uniqueness).
    private static bool EvidenceShowsUniqueValues(ColumnSchema col, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return false;

        var pattern = new Regex(
            $@"\b{Regex.Escape(col.SnakeCaseName)}\s*=\s*([^,;\s]+)",
            RegexOptions.IgnoreCase);
        var matches = pattern.Matches(evidence);

        if (matches.Count < 2) return false;

        var values = matches.Select(m => m.Groups[1].Value.ToLowerInvariant()).ToList();
        return values.Count == values.Distinct().Count();
    }

    private static bool HasDuplicateWarningForColumn(ColumnSchema col, IReadOnlyList<ValidationWarning> warnings) =>
        warnings.Any(w =>
            w.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
            w.ColumnName != null &&
            w.ColumnName.Equals(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase));

    private static bool IsTypeInferenceClaimValid(ColumnSchema? col)
    {
        if (col is null) return true;
        return col.InferredType != PostgresType.Numeric && col.InferredType != PostgresType.Boolean;
    }

    private static bool IsDuplicateClaimValid(ColumnSchema? col, AiReviewRequest request)
    {
        if (col?.IsCandidateKey != true) return true;
        return HasDetectedDuplicates(col, request);
    }

    private static ColumnSchema? FindReferencedColumn(string combined, IReadOnlyList<ColumnSchema> columns)
        => columns.FirstOrDefault(c =>
            combined.Contains(c.SnakeCaseName.ToLowerInvariant()) ||
            combined.Contains(c.Name.ToLowerInvariant()));

    private static bool NullabilityEvidenceExists(ColumnSchema? col, AiReviewRequest request)
    {
        if (col is null) return true;

        if (col.IsNullable) return true;

        var hasNull = request.SheetPreview.Rows.Any(r =>
            r.TryGetValue(col.SnakeCaseName, out var v) && v is null);
        if (hasNull) return true;

        return request.ValidationResult.Warnings.Any(w =>
            (w.ColumnName == null || w.ColumnName.Equals(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase)) &&
            (w.Message.Contains("null", StringComparison.OrdinalIgnoreCase) ||
             w.Message.Contains("missing", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasDetectedDuplicates(ColumnSchema col, AiReviewRequest request)
    {
        if (request.ValidationResult.Warnings.Any(w =>
            w.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
            w.ColumnName != null &&
            w.ColumnName.Equals(col.SnakeCaseName, StringComparison.OrdinalIgnoreCase)))
            return true;

        var values = request.SheetPreview.Rows
            .Select(r => r.TryGetValue(col.SnakeCaseName, out var v) ? v : null)
            .Where(v => v != null)
            .ToList();

        return values.Count != values.Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }
}

internal static class AiReviewGroundedFallback
{
    private static readonly HashSet<string> BusinessIdentifierNames =
        new(StringComparer.OrdinalIgnoreCase) { "username", "user_name", "email", "user_email" };

    internal static AiReviewResult Apply(AiReviewResult result, AiReviewRequest request)
    {
        if (request.Mode != AiReviewMode.Dataset) return result;
        if (result.Risks.Count > 0 || result.Recommendations.Count > 0) return result;

        var candidateKeys = request.TableSchema.Columns.Where(c => c.IsCandidateKey).ToList();

        return new AiReviewResult
        {
            Summary         = BuildSummary(request, candidateKeys),
            Risks           = [],
            Recommendations = BuildRecommendations(candidateKeys),
        };
    }

    private static string BuildSummary(AiReviewRequest request, IReadOnlyList<ColumnSchema> candidateKeys)
    {
        var tableName   = request.TableSchema.TableName;
        var sheetName   = request.SheetPreview.SheetName;
        var columnCount = request.TableSchema.Columns.Count;

        var sb = new StringBuilder();
        sb.Append($"Schema review complete for '{tableName}'");
        if (!string.IsNullOrWhiteSpace(sheetName) && !sheetName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            sb.Append($" (sheet: {sheetName})");
        sb.Append($". The inferred schema has {columnCount} column{(columnCount == 1 ? "" : "s")}.");

        if (candidateKeys.Count > 0)
        {
            sb.Append($" '{candidateKeys[0].SnakeCaseName}' is selected as the recommended primary key.");
            if (candidateKeys.Count > 1)
            {
                var others = string.Join(", ", candidateKeys.Skip(1).Select(c => $"'{c.SnakeCaseName}'"));
                sb.Append($" Additional candidate key{(candidateKeys.Count > 2 ? "s" : "")} detected: {others}.");
            }
        }

        sb.Append(" No evidence-backed data quality risks remained after validation.");
        return sb.ToString();
    }

    private static IReadOnlyList<AiReviewRecommendation> BuildRecommendations(IReadOnlyList<ColumnSchema> candidateKeys)
    {
        if (candidateKeys.Count == 0) return [];

        var recs       = new List<AiReviewRecommendation>();
        var primaryKey = candidateKeys[0];

        recs.Add(new AiReviewRecommendation
        {
            Priority    = "LOW",
            Description = $"Use '{primaryKey.SnakeCaseName}' as the primary key for this table.",
            Action      = $"Add PRIMARY KEY constraint on {primaryKey.SnakeCaseName}",
            Evidence    = $"schema: {primaryKey.SnakeCaseName} is the system-selected candidate key",
        });

        foreach (var col in candidateKeys.Skip(1).Where(IsBusinessIdentifier))
        {
            recs.Add(new AiReviewRecommendation
            {
                Priority    = "LOW",
                Description = $"Consider a UNIQUE constraint on '{col.SnakeCaseName}' only if {col.SnakeCaseName} values must be business-unique.",
                Action      = $"Add UNIQUE constraint on {col.SnakeCaseName} if uniqueness is a business requirement",
                Evidence    = $"schema: {col.SnakeCaseName} is a candidate key column",
            });
        }

        return recs;
    }

    private static bool IsBusinessIdentifier(ColumnSchema col)
        => BusinessIdentifierNames.Contains(col.SnakeCaseName) ||
           BusinessIdentifierNames.Contains(col.Name);
}
