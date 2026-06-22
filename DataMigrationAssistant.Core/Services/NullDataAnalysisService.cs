using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class NullDataAnalysisService : IDataAnalysisService
{
    public Task<DataAnalysisResult> AnalyzeAsync(
        DataAnalysisRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new DataAnalysisResult());
}
