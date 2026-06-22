namespace DataMigrationAssistant.Core.Models;

public sealed class ColumnInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SnakeCaseName { get; init; } = string.Empty;
}
