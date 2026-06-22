using DataMigrationAssistant.Core.Models;
using System.Text;

namespace DataMigrationAssistant.Core.Agents;

public sealed class ValidationAgent : IMigrationAgent
{
    public string Name => "Validation Agent";

    public bool CanHandle(string question)
    {
        var lower = question.ToLowerInvariant();
        return lower.Contains("validation")
            || lower.Contains("warning")
            || lower.Contains("proceed")
            || lower.Contains("duplicate")
            || lower.Contains("nullability");
    }

    public Task<MigrationAgentResponse> HandleAsync(
        MigrationAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var validation = context.ChatContext.Validation;

        if (validation is null)
        {
            return Task.FromResult(new MigrationAgentResponse
            {
                AgentName         = Name,
                Answer            = "No validation has been run yet. Run Validation on the Validation tab first.",
                WasHandledLocally = true,
            });
        }

        var answer = BuildAnswer(context.Question, validation);

        return Task.FromResult(new MigrationAgentResponse
        {
            AgentName         = Name,
            Answer            = answer,
            Sources           = ["Validation results"],
            WasHandledLocally = true,
        });
    }

    private static string BuildAnswer(string question, ValidationResult validation)
    {
        var lower = question.ToLowerInvariant();

        if (lower.Contains("proceed"))
            return BuildCanProceedAnswer(validation);

        if (lower.Contains("duplicate"))
            return BuildDuplicateAnswer(validation);

        if (lower.Contains("nullability"))
            return BuildNullabilityWarningsAnswer(validation);

        return BuildFullSummary(validation);
    }

    private static string BuildCanProceedAnswer(ValidationResult validation)
    {
        var status = validation.CanProceed ? "Yes" : "No";
        var detail = validation.HasWarnings
            ? $" — {validation.Warnings.Count} warning(s) present."
            : " — no warnings.";
        return $"Can proceed: {status}{detail}";
    }

    private static string BuildDuplicateAnswer(ValidationResult validation)
    {
        var dupes = validation.Warnings
            .Where(w => w.Code.Contains("DUPLICATE", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (dupes.Count == 0)
            return "No duplicate-related warnings were found.";

        var sb = new StringBuilder();
        sb.AppendLine("Duplicate warnings:");
        foreach (var w in dupes)
        {
            var col = w.ColumnName is not null ? $" (column: `{w.ColumnName}`)" : string.Empty;
            sb.AppendLine($"- [{w.Severity}] {w.Code}: {w.Message}{col}");
            if (w.Suggestion is not null)
                sb.AppendLine($"  Suggestion: {w.Suggestion}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildNullabilityWarningsAnswer(ValidationResult validation)
    {
        var nullWarnings = validation.Warnings
            .Where(w => w.Code.Contains("NULL", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (nullWarnings.Count == 0)
            return "No nullability-related warnings were found.";

        var sb = new StringBuilder();
        sb.AppendLine("Nullability warnings:");
        foreach (var w in nullWarnings)
        {
            var col = w.ColumnName is not null ? $" (column: `{w.ColumnName}`)" : string.Empty;
            sb.AppendLine($"- [{w.Severity}] {w.Code}: {w.Message}{col}");
            if (w.Suggestion is not null)
                sb.AppendLine($"  Suggestion: {w.Suggestion}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildFullSummary(ValidationResult validation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Can proceed: {(validation.CanProceed ? "Yes" : "No")}");

        if (!validation.HasWarnings)
        {
            sb.AppendLine("No warnings — data looks clean.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"{validation.Warnings.Count} warning(s):");
        foreach (var w in validation.Warnings)
        {
            var col = w.ColumnName is not null ? $" (column: `{w.ColumnName}`)" : string.Empty;
            sb.AppendLine($"- [{w.Severity}] {w.Code}: {w.Message}{col}");
            if (w.Suggestion is not null)
                sb.AppendLine($"  Suggestion: {w.Suggestion}");
        }

        return sb.ToString().TrimEnd();
    }
}
