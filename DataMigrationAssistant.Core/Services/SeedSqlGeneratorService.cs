using DataMigrationAssistant.Core.Generators;
using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class SeedSqlGeneratorService : ISeedSqlGeneratorService
{
    public string GenerateSeed(SheetPreview preview, TableSchema schema)
    {
        if (preview.Rows.Count == 0)
            return $"-- No data rows found in sheet '{preview.SheetName}'.";

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
        return $"INSERT INTO {schema.TableName} ({columnList})\nVALUES\n{body}\nON CONFLICT DO NOTHING;";
    }
}
