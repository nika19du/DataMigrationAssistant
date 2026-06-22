using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class NullAiReviewService : IAiReviewService
{
    public Task<AiReviewResult> ReviewAsync(AiReviewRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AiReviewResult());
}
