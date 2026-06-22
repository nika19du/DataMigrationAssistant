namespace DataMigrationAssistant.Core.Models;

public sealed class GtnSeedWarning
{
    public int RowNumber { get; init; }
    public string? ScenarioId { get; init; }
    public string Column { get; init; } = string.Empty;
    public string? Value { get; init; }
    public string Message { get; init; } = string.Empty;
}
