namespace DataMigrationAssistant.Core.Models;

public sealed class GtnScenarioRow
{
    public int RowNumber { get; init; }
    public string? ValidationScenarioId { get; init; }
    public string? Group { get; init; }
    public string? ValidationScenarioLabel { get; init; }
    public string? ValidationScenarioLogic { get; init; }
    public string? ValidationScenarioRulePlatformDataPoints { get; init; }
    public string? SystemElementType { get; init; }
    public string? ElementSubType { get; init; }
    public string? ElementRule1 { get; init; }
    public string? ElementRule2 { get; init; }
    public string? ElementRule3 { get; init; }
    public string? EeStatus { get; init; }
    public string? ManuallyValidationByOvRequired { get; init; }
    public string? SystemValidated { get; init; }
}
