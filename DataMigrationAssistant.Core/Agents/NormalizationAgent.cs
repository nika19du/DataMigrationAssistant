using DataMigrationAssistant.Core.Models;
using System.Text;

namespace DataMigrationAssistant.Core.Agents;

public sealed class NormalizationAgent : IMigrationAgent
{
    public string Name => "Normalization Agent";

    public bool CanHandle(string question)
    {
        var lower = question.ToLowerInvariant();
        return lower.Contains("normalization")
            || lower.Contains("normalize")
            || lower.Contains("foreign key")
            || lower.Contains("relationship")
            || lower.Contains("proposed table")
            || (lower.Contains("split") && lower.Contains("table"));
    }

    public Task<MigrationAgentResponse> HandleAsync(
        MigrationAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var proposal = context.ChatContext.NormalizationProposal;

        if (proposal is null || proposal.Tables.Count == 0)
        {
            return Task.FromResult(new MigrationAgentResponse
            {
                AgentName         = Name,
                Answer            = "No normalization proposal is currently available. Run Normalize first.",
                WasHandledLocally = true,
            });
        }

        var answer = BuildAnswer(context.Question, proposal);

        return Task.FromResult(new MigrationAgentResponse
        {
            AgentName         = Name,
            Answer            = answer,
            Sources           = ["Normalization proposal"],
            WasHandledLocally = true,
        });
    }

    private static string BuildAnswer(string question, NormalizationProposal proposal)
    {
        var lower = question.ToLowerInvariant();

        if (lower.Contains("foreign key") || lower.Contains("relationship"))
            return BuildForeignKeyAnswer(proposal);

        if (lower.Contains("proposed table"))
            return BuildTablesAnswer(proposal);

        return BuildFullProposal(proposal);
    }

    private static string BuildForeignKeyAnswer(NormalizationProposal proposal)
    {
        var sb    = new StringBuilder();
        var hasFks = false;

        foreach (var table in proposal.Tables)
        {
            var fks = table.Columns.Where(c => c.ForeignKeyTo is not null).ToList();
            if (fks.Count == 0) continue;

            hasFks = true;
            sb.AppendLine($"Table `{table.TableName}`:");
            foreach (var fk in fks)
                sb.AppendLine($"  - `{fk.Name}` → {fk.ForeignKeyTo}");
        }

        return hasFks
            ? sb.ToString().TrimEnd()
            : "No foreign key relationships are defined in the normalization proposal.";
    }

    private static string BuildTablesAnswer(NormalizationProposal proposal)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Proposed tables ({proposal.Tables.Count}):");
        foreach (var table in proposal.Tables)
            sb.AppendLine($"- `{table.TableName}` ({table.Columns.Count} columns)");
        return sb.ToString().TrimEnd();
    }

    private static string BuildFullProposal(NormalizationProposal proposal)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(proposal.Reasoning))
        {
            sb.AppendLine("Reasoning:");
            sb.AppendLine(proposal.Reasoning);
            sb.AppendLine();
        }

        sb.AppendLine($"Proposed tables ({proposal.Tables.Count}):");
        foreach (var table in proposal.Tables)
        {
            sb.AppendLine();
            sb.AppendLine($"**{table.TableName}**");

            var pk = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
            if (pk is not null)
                sb.AppendLine($"  Primary key: `{pk.Name}`");

            sb.AppendLine("  Columns:");
            foreach (var col in table.Columns)
            {
                var nullable = col.IsNullable ? "NULL" : "NOT NULL";
                var fk       = col.ForeignKeyTo is not null ? $" → {col.ForeignKeyTo}" : string.Empty;
                var pkMark   = col.IsPrimaryKey ? " (PK)" : string.Empty;
                sb.AppendLine($"    - `{col.Name}`: {col.PostgresType}, {nullable}{pkMark}{fk}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
