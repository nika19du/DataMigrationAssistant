using System.Text;
using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class DiffReportGeneratorService : IDiffReportGeneratorService
{
    public string GenerateReport(SeedDiffResult diff)
    {
        var added     = diff.Rows.Where(r => r.Status == SeedDiffStatus.Added).ToList();
        var removed   = diff.Rows.Where(r => r.Status == SeedDiffStatus.Removed).ToList();
        var changed   = diff.Rows.Where(r => r.Status == SeedDiffStatus.Changed).ToList();
        var unchanged = diff.Rows.Count(r => r.Status == SeedDiffStatus.Unchanged);

        var sb = new StringBuilder();

        sb.AppendLine($"# Seed Diff: {diff.TableName}");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("| Status    | Count |");
        sb.AppendLine("|-----------|-------|");
        sb.AppendLine($"| Added     | {added.Count} |");
        sb.AppendLine($"| Removed   | {removed.Count} |");
        sb.AppendLine($"| Changed   | {changed.Count} |");
        sb.AppendLine($"| Unchanged | {unchanged} |");

        if (added.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Added ({added.Count})");
            sb.AppendLine();
            foreach (var row in added)
                sb.AppendLine($"- `{row.KeyValue}`");
        }

        if (removed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Removed ({removed.Count})");
            sb.AppendLine();
            foreach (var row in removed)
                sb.AppendLine($"- `{row.KeyValue}`");
        }

        if (changed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Changed ({changed.Count})");

            foreach (var row in changed)
            {
                sb.AppendLine();
                sb.AppendLine($"### Row `{row.KeyValue}`");
                sb.AppendLine();
                sb.AppendLine("| Column | Old Value | New Value |");
                sb.AppendLine("|--------|-----------|-----------|");
                foreach (var ch in row.Changes)
                    sb.AppendLine(
                        $"| {Cell(ch.ColumnName)} | {CellOrNull(ch.OldValue)} | {CellOrNull(ch.NewValue)} |");
            }
        }

        return sb.ToString().TrimEnd();
    }

    // Renders a nullable value: null → "NULL", otherwise escaped for markdown table syntax.
    private static string CellOrNull(string? value) =>
        value is null ? "NULL" : Cell(value);

    // Escapes characters that would break a markdown table cell.
    private static string Cell(string value) =>
        value.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}
