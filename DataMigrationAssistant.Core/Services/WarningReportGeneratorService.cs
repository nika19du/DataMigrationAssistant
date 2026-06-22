using System.Text;
using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class WarningReportGeneratorService : IWarningReportGeneratorService
{
    public string GenerateGtnWarningReport(IReadOnlyList<GtnSeedWarning> warnings, int scenarioCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# GTN Scenario Seed — Warnings");
        sb.AppendLine();
        sb.AppendLine($"Scenarios generated: {scenarioCount}");
        sb.AppendLine($"Warnings: {warnings.Count}");
        sb.AppendLine();

        if (warnings.Count == 0)
        {
            sb.Append("No warnings.");
            return sb.ToString();
        }

        sb.AppendLine("| Row | Scenario ID | Column | Value | Message |");
        sb.AppendLine("|-----|-------------|--------|-------|---------|");

        foreach (var w in warnings)
        {
            var scenId = w.ScenarioId ?? "—";
            var val    = EscapeCell(w.Value ?? "—");
            var msg    = EscapeCell(w.Message);
            sb.AppendLine($"| {w.RowNumber} | {scenId} | {w.Column} | {val} | {msg} |");
        }

        return sb.ToString();
    }

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
             .Replace("\n", " ", StringComparison.Ordinal)
             .Replace("\r", string.Empty, StringComparison.Ordinal);
}
