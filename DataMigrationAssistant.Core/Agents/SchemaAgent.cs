using DataMigrationAssistant.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace DataMigrationAssistant.Core.Agents;

public sealed class SchemaAgent : IMigrationAgent
{
    // Matches "nullable" but not "nullability" (handled by ValidationAgent)
    private static readonly Regex NullableWordPattern =
        new(@"\bnullable\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MultipleKeyPattern =
        new(@"The first \((\w+)\) will be used", RegexOptions.Compiled);

    // Recommendation-intent words that signal DataAnalysisAgent should own the question
    private static readonly string[] RecommendationIntentWords =
        ["should", "recommend", "recommended", "best", "suggest", "prefer", "use"];

    // Key-related terms; combined with recommendation intent → DataAnalysisAgent
    private static readonly string[] KeyRelatedTerms =
        ["primary key", "candidate key", "unique constraint", "key"];

    public string Name => "Schema Agent";

    public bool CanHandle(string question)
    {
        var lower = question.ToLowerInvariant();

        // Recommendation questions about keys belong to DataAnalysisAgent
        bool hasRecommendationIntent = RecommendationIntentWords.Any(w => lower.Contains(w));
        bool hasKeyTerm              = KeyRelatedTerms.Any(t => lower.Contains(t));
        if (hasRecommendationIntent && hasKeyTerm)
            return false;

        return lower.Contains("schema")
            || lower.Contains("column")
            || lower.Contains("type")
            || lower.Contains("candidate key")
            || lower.Contains("primary key")
            || NullableWordPattern.IsMatch(question);
    }

    public Task<MigrationAgentResponse> HandleAsync(
        MigrationAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var schema = context.ChatContext.Schema;

        if (schema is null)
        {
            return Task.FromResult(new MigrationAgentResponse
            {
                AgentName         = Name,
                Answer            = "No schema has been inferred yet. Run Schema Inference on the Schema tab first.",
                WasHandledLocally = true,
            });
        }

        var answer = BuildAnswer(context.Question, schema, context.ChatContext.Validation);

        return Task.FromResult(new MigrationAgentResponse
        {
            AgentName         = Name,
            Answer            = answer,
            Sources           = ["Inferred schema"],
            WasHandledLocally = true,
        });
    }

    private static string BuildAnswer(string question, TableSchema schema, ValidationResult? validation)
    {
        var lower = question.ToLowerInvariant();

        if (lower.Contains("primary key"))
            return BuildPrimaryKeyAnswer(schema, validation);

        if (lower.Contains("candidate key"))
            return BuildCandidateKeyAnswer(schema);

        if (NullableWordPattern.IsMatch(question))
            return BuildNullabilityAnswer(schema);

        if (lower.Contains("type"))
            return BuildTypeAnswer(schema);

        return BuildFullSchemaAnswer(schema);
    }

    private static string BuildPrimaryKeyAnswer(TableSchema schema, ValidationResult? validation)
    {
        var candidates = schema.Columns
            .Where(c => c.IsCandidateKey)
            .OrderBy(c => c.Index)
            .ToList();

        if (candidates.Count == 0)
            return "No candidate keys were found in the inferred schema. Consider adding a surrogate key (e.g., a serial `id` column).";

        string? recommended = null;
        if (validation is not null)
        {
            var warning = validation.Warnings.FirstOrDefault(w => w.Code == "MULTIPLE_CANDIDATE_KEYS");
            if (warning is not null)
            {
                var match = MultipleKeyPattern.Match(warning.Message);
                if (match.Success)
                    recommended = match.Groups[1].Value;
            }
        }
        recommended ??= candidates[0].SnakeCaseName;

        var sb = new StringBuilder();
        sb.AppendLine($"Recommended primary key: `{recommended}`");

        if (candidates.Count > 1)
        {
            var others = candidates
                .Where(c => c.SnakeCaseName != recommended)
                .Select(c => $"`{c.SnakeCaseName}`");
            sb.AppendLine($"Other candidate keys: {string.Join(", ", others)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildCandidateKeyAnswer(TableSchema schema)
    {
        var keys = schema.Columns.Where(c => c.IsCandidateKey).ToList();

        if (keys.Count == 0)
            return "No candidate keys were found in the inferred schema.";

        var sb = new StringBuilder();
        sb.AppendLine("Candidate keys (non-null and unique in the sample):");
        foreach (var k in keys)
            sb.AppendLine($"- `{k.SnakeCaseName}` ({k.InferredType})");

        return sb.ToString().TrimEnd();
    }

    private static string BuildNullabilityAnswer(TableSchema schema)
    {
        var nullable    = schema.Columns.Where(c =>  c.IsNullable).ToList();
        var notNullable = schema.Columns.Where(c => !c.IsNullable).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"NOT NULL columns ({notNullable.Count}): " +
            (notNullable.Count == 0 ? "none" : string.Join(", ", notNullable.Select(c => $"`{c.SnakeCaseName}`"))));
        sb.AppendLine($"Nullable columns ({nullable.Count}): " +
            (nullable.Count == 0 ? "none" : string.Join(", ", nullable.Select(c => $"`{c.SnakeCaseName}`"))));

        return sb.ToString().TrimEnd();
    }

    private static string BuildTypeAnswer(TableSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Inferred column types:");
        foreach (var col in schema.Columns)
            sb.AppendLine($"- `{col.SnakeCaseName}`: {col.InferredType}");

        return sb.ToString().TrimEnd();
    }

    private static string BuildFullSchemaAnswer(TableSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Table: `{schema.TableName}`");
        sb.AppendLine($"Sampled rows: {schema.SampleRowCount}");
        sb.AppendLine();
        sb.AppendLine("Columns:");
        foreach (var col in schema.Columns)
        {
            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            var key      = col.IsCandidateKey ? " (candidate key)" : string.Empty;
            var dupes    = col.HasDuplicates ? " [has duplicates]" : string.Empty;
            sb.AppendLine($"- `{col.SnakeCaseName}` — {col.InferredType}, {nullable}{key}{dupes}");
        }

        return sb.ToString().TrimEnd();
    }
}
