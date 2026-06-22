using DataMigrationAssistant.Core.Models;
using System.Text;

namespace DataMigrationAssistant.Core.Agents;

public sealed class GtnAgent : IMigrationAgent
{
    public string Name => "GTN Agent";

    public bool CanHandle(string question)
    {
        var lower = question.ToLowerInvariant();
        return lower.Contains("gtn")
            || lower.Contains("scenario")
            || lower.Contains("validation group")
            || lower.Contains("pay element")
            || lower.Contains("payroll")
            || lower.Contains("gtn seed");
    }

    public Task<MigrationAgentResponse> HandleAsync(
        MigrationAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var gtnResult = context.ChatContext.GtnResult;

        var answer = gtnResult is not null
            ? BuildResultSummary(gtnResult)
            : BuildMissingAnswer(context.ChatContext);

        var sources = gtnResult is not null
            ? (IReadOnlyList<string>)["GTN seed generation result"]
            : [];

        return Task.FromResult(new MigrationAgentResponse
        {
            AgentName         = Name,
            Answer            = answer,
            Sources           = sources,
            WasHandledLocally = true,
        });
    }

    private static string BuildResultSummary(GtnSeedGenerationResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GTN seed generation completed: **{result.ScenarioCount}** scenario(s) generated.");
        sb.AppendLine();

        if (result.Warnings.Count == 0)
        {
            sb.AppendLine("No warnings — all rows were processed cleanly.");
        }
        else
        {
            sb.AppendLine($"**{result.Warnings.Count}** warning(s) encountered:");
            foreach (var w in result.Warnings.Take(10))
            {
                var scenarioTag = w.ScenarioId is not null ? $" [scenario {w.ScenarioId}]" : string.Empty;
                sb.AppendLine($"- Row {w.RowNumber}{scenarioTag}: `{w.Column}` — {w.Message}");
            }

            if (result.Warnings.Count > 10)
                sb.AppendLine($"… and {result.Warnings.Count - 10} more. Download `gtn-seed-warnings.md` from the **Downloads** tab for the full list.");
        }

        sb.AppendLine();
        sb.AppendLine("Download **gtn-scenarios-seed.sql** from the **Downloads** tab to apply this seed.");

        return sb.ToString().TrimEnd();
    }

    private static string BuildMissingAnswer(ChatContext ctx)
    {
        var sb = new StringBuilder();

        if (ctx.Schema is not null)
            sb.AppendLine($"No GTN seed has been generated yet for `{ctx.Schema.TableName}`.");
        else
            sb.AppendLine("No GTN seed has been generated yet.");

        sb.AppendLine();
        sb.AppendLine("To generate GTN scenario seed SQL:");
        sb.AppendLine("1. Open the **Downloads** tab.");
        sb.AppendLine("2. In the **GTN Scenario Seed** section, click **Generate GTN Scenarios**.");
        sb.AppendLine();
        sb.AppendLine("The sheet must contain these columns:");
        sb.AppendLine("`validation_scenario_id`, `validation_scenario_label`, `system_element_type`, `element_sub_type`, `element_rule_1`");

        return sb.ToString().TrimEnd();
    }
}
