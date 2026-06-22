using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Normalization;

public interface IDeterministicNormalizationService
{
    ServiceResult<NormalizationProposal> TryNormalize(NormalizationRequest request);
}
