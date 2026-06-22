namespace DataMigrationAssistant.Core.Models;

public sealed class DataAnalysisFinding
{
    public string  Category    { get; init; } = string.Empty;
    public string  Severity    { get; init; } = string.Empty;
    public string  Description { get; init; } = string.Empty;
    public string? Detail      { get; init; }
}
