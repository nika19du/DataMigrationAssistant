namespace DataMigrationAssistant.Core.Models;

public sealed class ValidationWarning
{
    public string Code { get; init; } = string.Empty;
    public ValidationSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    /// <summary>Null for sheet-level warnings; set to the snake-case column name for column-level warnings.</summary>
    public string? ColumnName { get; init; }
    /// <summary>Optional actionable hint shown below the message in the UI. Null when no specific action is needed.</summary>
    public string? Suggestion { get; init; }
}
