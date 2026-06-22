using DataMigrationAssistant.Core.Models;
using System.Text;

namespace DataMigrationAssistant.Core.Agents;

public sealed class DataAnalysisAgent : IMigrationAgent
{
    public string Name => "Data Analysis Agent";

    // Matches "recommend", "recommended", "recommendation"
    private static readonly string[] RecommendationIntentWords =
        ["should", "recommend", "best", "suggest", "prefer", "use"];

    private static readonly string[] KeyRelatedTerms =
        ["primary key", "candidate key", "unique constraint", "key"];

    public bool CanHandle(string question)
    {
        var lower = question.ToLowerInvariant();

        if (lower.Contains("analysis")
            || lower.Contains("finding")
            || lower.Contains("risk")
            || lower.Contains("recommendation")
            || lower.Contains("data quality")
            || lower.Contains("unique constraint"))
            return true;

        // Recommendation intent combined with key terms → this agent owns it
        bool hasRecommendationIntent = RecommendationIntentWords.Any(w => lower.Contains(w));
        bool hasKeyTerm              = KeyRelatedTerms.Any(t => lower.Contains(t));
        return hasRecommendationIntent && hasKeyTerm;
    }

    public Task<MigrationAgentResponse> HandleAsync(
        MigrationAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var result = context.ChatContext.AnalysisResult;

        if (result is null)
        {
            return Task.FromResult(new MigrationAgentResponse
            {
                AgentName         = Name,
                Answer            = "Data Analysis has not been run yet. Open the Data Analysis tab and run analysis first.",
                WasHandledLocally = true,
            });
        }

        var answer = BuildAnswer(context.Question, result);

        return Task.FromResult(new MigrationAgentResponse
        {
            AgentName         = Name,
            Answer            = answer,
            Sources           = ["Data analysis results"],
            WasHandledLocally = true,
        });
    }

    private static string BuildAnswer(string question, DataAnalysisResult result)
    {
        var lower = question.ToLowerInvariant();

        if (lower.Contains("risk"))
            return BuildRisksAnswer(result);

        // "recommendation", "recommended", or recommendation-intent + key term
        if (lower.Contains("recommendation")
            || lower.Contains("recommended")
            || (RecommendationIntentWords.Any(w => lower.Contains(w)) && KeyRelatedTerms.Any(t => lower.Contains(t))))
            return BuildRecommendationsAnswer(result);

        if (lower.Contains("finding"))
            return BuildFindingsAnswer(result);

        return BuildFullSummary(result);
    }

    private static string BuildRisksAnswer(DataAnalysisResult result)
    {
        if (result.Risks.Count == 0)
            return "No risks were identified in the data analysis.";

        var sb = new StringBuilder();
        sb.AppendLine("Identified risks:");
        foreach (var risk in result.Risks)
        {
            sb.AppendLine($"- [{risk.Severity}] {risk.Category}: {risk.Description}");
            if (risk.Detail is not null)
                sb.AppendLine($"  Detail: {risk.Detail}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildRecommendationsAnswer(DataAnalysisResult result)
    {
        if (result.Recommendations.Count == 0)
            return "No recommendations were generated.";

        var sb = new StringBuilder();
        sb.AppendLine("Recommendations:");
        foreach (var rec in result.Recommendations)
            sb.AppendLine($"- [{rec.Priority}] {rec.Type}: {rec.Description}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildFindingsAnswer(DataAnalysisResult result)
    {
        if (result.Findings.Count == 0)
            return "No findings were recorded.";

        var sb = new StringBuilder();
        sb.AppendLine("Findings:");
        foreach (var f in result.Findings)
        {
            sb.AppendLine($"- [{f.Severity}] {f.Category}: {f.Description}");
            if (f.Detail is not null)
                sb.AppendLine($"  Detail: {f.Detail}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildFullSummary(DataAnalysisResult result)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            sb.AppendLine("Summary:");
            sb.AppendLine(result.Summary);
            sb.AppendLine();
        }

        if (result.Findings.Count > 0)
        {
            sb.AppendLine($"Findings ({result.Findings.Count}):");
            foreach (var f in result.Findings)
                sb.AppendLine($"- [{f.Severity}] {f.Category}: {f.Description}");
            sb.AppendLine();
        }

        if (result.Risks.Count > 0)
        {
            sb.AppendLine($"Risks ({result.Risks.Count}):");
            foreach (var r in result.Risks)
                sb.AppendLine($"- [{r.Severity}] {r.Category}: {r.Description}");
            sb.AppendLine();
        }

        if (result.Recommendations.Count > 0)
        {
            sb.AppendLine($"Recommendations ({result.Recommendations.Count}):");
            foreach (var rec in result.Recommendations)
                sb.AppendLine($"- [{rec.Priority}] {rec.Type}: {rec.Description}");
        }

        return sb.ToString().TrimEnd();
    }
}
