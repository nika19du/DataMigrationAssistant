namespace DataMigrationAssistant.Core.Models;

public sealed class TableSchema
{
    public string TableName { get; init; } = string.Empty;
    public string SheetName { get; init; } = string.Empty;
    public IReadOnlyList<ColumnSchema> Columns { get; init; } = [];
    public int SampleRowCount { get; init; }
}
