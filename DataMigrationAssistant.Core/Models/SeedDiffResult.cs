namespace DataMigrationAssistant.Core.Models;

public sealed class SeedDiffResult
{
    public string TableName { get; init; } = string.Empty;
    /// <summary>Snake-case name of the column used as the candidate key for this diff.</summary>
    public string KeyColumnName { get; init; } = string.Empty;
    public IReadOnlyList<SeedDiffRow> Rows { get; init; } = [];
}
