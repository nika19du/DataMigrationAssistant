using DataMigrationAssistant.Core.Models;
using System.Text;

namespace DataMigrationAssistant.Core.Agents;

public sealed class SqlGenerationAgent : IMigrationAgent
{
    public string Name => "SQL Agent";

    public bool CanHandle(string question)
    {
        var lower = question.ToLowerInvariant();
        return lower.Contains("sql")
            || lower.Contains("create table")
            || lower.Contains("insert")
            || lower.Contains("upsert")
            || lower.Contains("seed")
            || lower.Contains("migration sql")
            || lower.Contains("migration script")
            || lower.Contains("diff")
            || lower.Contains("difference")
            || lower.Contains("generated file")
            || lower.Contains("download");
    }

    public Task<MigrationAgentResponse> HandleAsync(
        MigrationAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var ctx     = context.ChatContext;
        var answer  = BuildAnswer(ctx);
        var sources = BuildSources(ctx);

        return Task.FromResult(new MigrationAgentResponse
        {
            AgentName         = Name,
            Answer            = answer,
            Sources           = sources,
            WasHandledLocally = true,
        });
    }

    private static string BuildAnswer(ChatContext ctx)
    {
        bool hasSeedSql      = ctx.GeneratedSeedSql is not null;
        bool hasMigrationSql = ctx.GeneratedMigrationSql is not null;
        bool hasGtnSql       = ctx.GtnResult is not null;

        if (!hasSeedSql && !hasMigrationSql && !hasGtnSql)
            return BuildNotGeneratedAnswer(ctx);

        return BuildGeneratedSummary(ctx, hasSeedSql, hasMigrationSql, hasGtnSql);
    }

    private static string BuildNotGeneratedAnswer(ChatContext ctx)
    {
        var sb = new StringBuilder();

        if (ctx.Schema is null)
        {
            sb.AppendLine("No SQL has been generated yet. Load a file and infer the schema first, then use the **Downloads** tab to generate SQL files.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"No SQL has been generated yet for `{ctx.Schema.TableName}`.");
        sb.AppendLine();
        sb.AppendLine("Use the **Downloads** tab to generate:");
        sb.AppendLine("- **seed.sql** — INSERT statements for every row");
        sb.AppendLine("- **normalized-schema.sql** / **normalized-seed.sql** — after running Normalization");
        sb.AppendLine("- **gtn-scenarios-seed.sql** — if this is a GTN validation scenario sheet");

        return sb.ToString().TrimEnd();
    }

    private static string BuildGeneratedSummary(
        ChatContext ctx,
        bool hasSeedSql,
        bool hasMigrationSql,
        bool hasGtnSql)
    {
        var sb        = new StringBuilder();
        var tableName = ctx.Schema?.TableName ?? "the current sheet";

        sb.AppendLine($"SQL has been generated for `{tableName}`:");
        sb.AppendLine();

        if (hasSeedSql)
        {
            var lines = ctx.GeneratedSeedSql!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            sb.AppendLine($"- **seed.sql** — {lines} line(s) of INSERT statements");
        }

        if (hasMigrationSql)
        {
            var lines = ctx.GeneratedMigrationSql!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            sb.AppendLine($"- **normalized-schema.sql** — {lines} line(s) of CREATE TABLE statements");
        }

        if (hasGtnSql)
        {
            var gtn     = ctx.GtnResult!;
            var warnPart = gtn.Warnings.Count > 0
                ? $", {gtn.Warnings.Count} warning(s)"
                : ", no warnings";
            sb.AppendLine($"- **gtn-scenarios-seed.sql** — {gtn.ScenarioCount} scenario(s){warnPart}");
        }

        sb.AppendLine();
        sb.AppendLine("Download these files from the **Downloads** tab. No SQL is executed — all generation is in-memory.");

        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> BuildSources(ChatContext ctx)
    {
        var sources = new List<string>();
        if (ctx.GeneratedSeedSql is not null)      sources.Add("seed.sql");
        if (ctx.GeneratedMigrationSql is not null) sources.Add("normalized-schema.sql");
        if (ctx.GtnResult is not null)             sources.Add("gtn-scenarios-seed.sql");
        return sources;
    }
}
