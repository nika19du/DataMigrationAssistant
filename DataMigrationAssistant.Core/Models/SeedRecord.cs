namespace DataMigrationAssistant.Core.Models;

public sealed class SeedRecord
{
    public string TableName { get; init; } = string.Empty;
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; } = [];
}
