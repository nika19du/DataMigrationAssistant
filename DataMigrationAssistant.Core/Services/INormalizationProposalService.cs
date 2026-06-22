using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public interface INormalizationProposalService
{
    Task<ServiceResult<NormalizationProposal>> ProposeAsync(
        NormalizationRequest request,
        CancellationToken cancellationToken = default);
}
