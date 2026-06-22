using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class SqlGeneratorService : ISqlGeneratorService
{
    public string GenerateCreateTable(TableSchema schema)
    {
        if (schema.Columns.Count == 0)
            return $"CREATE TABLE IF NOT EXISTS {schema.TableName} (\n);";

        int nameWidth = schema.Columns.Max(c => c.SnakeCaseName.Length) + 2;
        int typeWidth = schema.Columns.Max(c => ToSqlType(c.InferredType).Length) + 2;

        var candidateKey = schema.Columns.FirstOrDefault(c => c.IsCandidateKey);
        var innerLines = new List<string>(schema.Columns.Count + 1);

        foreach (var col in schema.Columns)
        {
            var name = col.SnakeCaseName.PadRight(nameWidth);
            var type = ToSqlType(col.InferredType).PadRight(typeWidth);
            var constraint = col.IsNullable ? string.Empty : "NOT NULL";
            innerLines.Add($"    {name}{type}{constraint}".TrimEnd());
        }

        if (candidateKey is not null)
            innerLines.Add($"    PRIMARY KEY ({candidateKey.SnakeCaseName})");

        var body = string.Join(",\n", innerLines);
        return $"CREATE TABLE IF NOT EXISTS {schema.TableName} (\n{body}\n);";
    }

    private static string ToSqlType(PostgresType type) => type switch
    {
        PostgresType.Boolean   => "BOOLEAN",
        PostgresType.Integer   => "INTEGER",
        PostgresType.BigInt    => "BIGINT",
        PostgresType.Numeric   => "NUMERIC",
        PostgresType.Date      => "DATE",
        PostgresType.Timestamp => "TIMESTAMP",
        PostgresType.Text      => "TEXT",
        _                      => "TEXT",
    };
}
