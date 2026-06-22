namespace DataMigrationAssistant.Core.Models;

public sealed class SeedDiffCellChange
{
    public string ColumnName { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}
