using System.Text;
using DataMigrationAssistant.Core.Generators;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public sealed class UpsertSqlGeneratorService : IUpsertSqlGeneratorService
{
    public ServiceResult<string> GenerateUpsert(SheetPreview preview, TableSchema schema)
    {
        var conflictKey = schema.Columns.FirstOrDefault(c => c.IsCandidateKey);
        if (conflictKey is null)
            return ServiceResult<string>.Fail(
                $"Cannot generate UPSERT for '{schema.TableName}': no candidate key detected. " +
                "Ensure at least one column has unique, non-null values.");

        var updateColumns = schema.Columns.Where(c => !c.IsCandidateKey).ToList();
        if (updateColumns.Count == 0)
            return ServiceResult<string>.Fail(
                $"Cannot generate UPSERT for '{schema.TableName}': every column is part of the conflict key — " +
                "there are no columns to update. Use seed-sql instead.");

        if (preview.Rows.Count == 0)
            return ServiceResult<string>.Ok($"-- No data rows found in sheet '{preview.SheetName}'.");

        var columns    = schema.Columns;
        var columnList = string.Join(", ", columns.Select(c => c.SnakeCaseName));

        var valueRows = preview.Rows.Select(row =>
        {
            var values = columns.Select(col =>
            {
                var raw = row.TryGetValue(col.SnakeCaseName, out var v) ? v : null;
                return SqlValueFormatter.Format(raw, col.InferredType);
            });
            return $"    ({string.Join(", ", values)})";
        }).ToList();

        var body = string.Join(",\n", valueRows);

        var updateSet = string.Join(",\n", updateColumns.Select(c =>
            $"    {c.SnakeCaseName} = EXCLUDED.{c.SnakeCaseName}"));

        var sb = new StringBuilder();
        sb.Append($"INSERT INTO {schema.TableName} ({columnList})\n");
        sb.Append("VALUES\n");
        sb.Append(body);
        sb.Append($"\nON CONFLICT ({conflictKey.SnakeCaseName})\n");
        sb.Append("DO UPDATE SET\n");
        sb.Append(updateSet);
        sb.Append(';');

        return ServiceResult<string>.Ok(sb.ToString());
    }
}
