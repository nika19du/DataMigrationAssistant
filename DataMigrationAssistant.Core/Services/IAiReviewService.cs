using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface IAiReviewService
{
    Task<AiReviewResult> ReviewAsync(AiReviewRequest request, CancellationToken cancellationToken = default);
}
