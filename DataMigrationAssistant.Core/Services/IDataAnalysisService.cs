using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface IDataAnalysisService
{
    Task<DataAnalysisResult> AnalyzeAsync(
        DataAnalysisRequest request,
        CancellationToken cancellationToken = default);
}
