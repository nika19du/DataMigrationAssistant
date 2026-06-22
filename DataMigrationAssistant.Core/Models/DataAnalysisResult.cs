namespace DataMigrationAssistant.Core.Models;

public sealed class DataAnalysisResult
{
    public string                                Summary         { get; init; } = string.Empty;
    public IReadOnlyList<DataAnalysisFinding>    Findings        { get; init; } = [];
    public IReadOnlyList<DataAnalysisFinding>    Risks           { get; init; } = [];
    public IReadOnlyList<DataAnalysisRecommendation> Recommendations { get; init; } = [];
}
