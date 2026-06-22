namespace DataMigrationAssistant.Core.Models;

public sealed class ProposedTable
{
    public string TableName { get; init; } = string.Empty;
    public IReadOnlyList<ProposedColumn> Columns { get; init; } = [];
    public IReadOnlyList<string> SourceColumns { get; init; } = [];
    public string CreateTableSql { get; init; } = string.Empty;
    public string SeedSql { get; init; } = string.Empty;
    /// <summary>Starting value for the auto-sequence used when generating surrogate PK seed values. Defaults to 1.</summary>
    public int SeedSequenceStart { get; init; } = 1;
}
