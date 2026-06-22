namespace DataMigrationAssistant.Core.Models;

public sealed class SeedDiffRow
{
    public SeedDiffStatus Status { get; init; }
    /// <summary>Normalized value of the candidate key column that identifies this row.</summary>
    public string KeyValue { get; init; } = string.Empty;
    /// <summary>Per-column changes; populated only for Changed rows.</summary>
    public IReadOnlyList<SeedDiffCellChange> Changes { get; init; } = [];
    /// <summary>Full normalized column values for this row; populated only for Added rows.</summary>
    public IReadOnlyDictionary<string, string?> NewRowValues { get; init; } = new Dictionary<string, string?>();
}
