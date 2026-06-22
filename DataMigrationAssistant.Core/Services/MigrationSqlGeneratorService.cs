using System.Text;
using DataMigrationAssistant.Core.Generators;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public sealed class MigrationSqlGeneratorService : IMigrationSqlGeneratorService
{
    public ServiceResult<string> GenerateMigration(SeedDiffResult diff, TableSchema schema)
    {
        if (string.IsNullOrWhiteSpace(diff.KeyColumnName))
            return ServiceResult<string>.Fail(
                $"Cannot generate migration for '{diff.TableName}': key column name is missing.");

        var added     = diff.Rows.Where(r => r.Status == SeedDiffStatus.Added).ToList();
        var changed   = diff.Rows.Where(r => r.Status == SeedDiffStatus.Changed).ToList();
        var removed   = diff.Rows.Where(r => r.Status == SeedDiffStatus.Removed).ToList();
        var unchanged = diff.Rows.Count(r => r.Status == SeedDiffStatus.Unchanged);

        var columnsByName = schema.Columns.ToDictionary(c => c.SnakeCaseName);
        columnsByName.TryGetValue(diff.KeyColumnName, out var keyColumn);
        var keyType = keyColumn?.InferredType ?? PostgresType.Text;

        var sb = new StringBuilder();

        // Summary header
        sb.AppendLine($"-- Migration for: {diff.TableName}");
        sb.AppendLine($"-- Added:     {added.Count}");
        sb.AppendLine($"-- Changed:   {changed.Count}");
        sb.AppendLine($"-- Removed:   {removed.Count} (not applied — review manually)");
        sb.AppendLine($"-- Unchanged: {unchanged}");

        // Removed rows — comments only, no DELETE
        if (removed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"-- === REMOVED ({removed.Count}) — review manually, no statements generated ===");
            foreach (var row in removed)
                sb.AppendLine($"-- {diff.KeyColumnName} = {SqlValueFormatter.Format(row.KeyValue, keyType)}");
        }

        // Added rows — INSERT ... ON CONFLICT (key) DO NOTHING
        if (added.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"-- === ADDED ({added.Count}) ===");
            var columnList = string.Join(", ", schema.Columns.Select(c => c.SnakeCaseName));

            foreach (var row in added)
            {
                var values = schema.Columns.Select(col =>
                {
                    row.NewRowValues.TryGetValue(col.SnakeCaseName, out var val);
                    return SqlValueFormatter.Format(val, col.InferredType);
                });
                sb.AppendLine($"INSERT INTO {diff.TableName} ({columnList})");
                sb.AppendLine($"VALUES ({string.Join(", ", values)})");
                sb.AppendLine($"ON CONFLICT ({diff.KeyColumnName}) DO NOTHING;");
                sb.AppendLine();
            }
        }

        // Changed rows — UPDATE SET (only changed columns) WHERE key
        if (changed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"-- === CHANGED ({changed.Count}) ===");

            foreach (var row in changed)
            {
                var changes = row.Changes;
                sb.AppendLine($"UPDATE {diff.TableName} SET");

                for (var i = 0; i < changes.Count; i++)
                {
                    var ch = changes[i];
                    var colType = columnsByName.TryGetValue(ch.ColumnName, out var col)
                        ? col.InferredType
                        : PostgresType.Text;
                    var comma = i < changes.Count - 1 ? "," : string.Empty;
                    sb.AppendLine($"    {ch.ColumnName} = {SqlValueFormatter.Format(ch.NewValue, colType)}{comma}");
                }

                sb.AppendLine($"WHERE {diff.KeyColumnName} = {SqlValueFormatter.Format(row.KeyValue, keyType)};");
                sb.AppendLine();
            }
        }

        return ServiceResult<string>.Ok(sb.ToString().TrimEnd());
    }
}
