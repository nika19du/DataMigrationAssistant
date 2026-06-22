namespace DataMigrationAssistant.Core.Models;

public sealed class ValidationResult
{
    /// <summary>
    /// False only when the sheet cannot produce any useful output — specifically, when it has no columns.
    /// All other conditions produce warnings while still allowing processing to continue.
    /// </summary>
    public bool CanProceed { get; init; } = true;
    public IReadOnlyList<ValidationWarning> Warnings { get; init; } = [];
    public bool HasWarnings => Warnings.Count > 0;
}
