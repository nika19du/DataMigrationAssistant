using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public sealed class NullNormalizationService : INormalizationProposalService
{
    public Task<ServiceResult<NormalizationProposal>> ProposeAsync(
        NormalizationRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult<NormalizationProposal>.Fail(
            "No AI normalization provider configured."));
}
