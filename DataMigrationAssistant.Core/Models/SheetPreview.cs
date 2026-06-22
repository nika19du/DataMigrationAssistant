namespace DataMigrationAssistant.Core.Models;

public sealed class SheetPreview
{
    public string SheetName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows { get; init; } = [];
    public int TotalRowCount { get; init; }
    public int HeaderRowNumber { get; init; }
}
