namespace DataMigrationAssistant.Core.Models;

public sealed class ColumnSchema
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SnakeCaseName { get; init; } = string.Empty;
    public PostgresType InferredType { get; init; }
    public bool IsNullable { get; init; }
    public bool HasDuplicates { get; init; }
    /// <summary>True when the column is non-nullable and all sample values are unique (structural check only).</summary>
    public bool IsCandidateKey { get; init; }
    /// <summary>Semantic quality of this column as a database key, combining type fitness and naming convention.</summary>
    public CandidateKeyQuality CandidateKeyQuality { get; init; }
}
