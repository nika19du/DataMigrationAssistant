using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Normalization;

public interface IDeterministicNormalizationRule
{
    bool CanHandle(NormalizationRequest request);
    NormalizationProposal Apply(NormalizationRequest request);
}
