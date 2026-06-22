namespace DataMigrationAssistant.Core.Models;

public sealed class NormalizedGtnScenario
{
    public int Id { get; init; }
    public string ValidationScenarioId { get; init; } = string.Empty;
    public int? ValidationGroup { get; init; }
    public string? ValidationScenarioLabel { get; init; }
    public string? ValidationScenarioLogic { get; init; }
    public string? ValidationScenarioRule { get; init; }
    public int? ElementSubtype { get; init; }
    public int? ElementRule1 { get; init; }
    public int? ElementRule2 { get; init; }
    public int? ElementRule3 { get; init; }
    public IReadOnlyList<int> SystemPayElements { get; init; } = [];
    public IReadOnlyList<int> AssignmentStatus { get; init; } = [];
    public int? SystemValidated { get; init; }
    public bool ManualValidationRequired { get; init; }
}
