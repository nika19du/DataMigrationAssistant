namespace DataMigrationAssistant.Core.Models;

public sealed class ProposedColumn
{
    public string Name { get; init; } = string.Empty;
    public string PostgresType { get; init; } = string.Empty;
    public bool IsNullable { get; init; }
    public bool IsPrimaryKey { get; init; }
    public string? ForeignKeyTo { get; init; }
}
