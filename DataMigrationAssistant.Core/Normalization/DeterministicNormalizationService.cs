using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Normalization;

public sealed class DeterministicNormalizationService : IDeterministicNormalizationService
{
    private readonly IReadOnlyList<IDeterministicNormalizationRule> _rules;

    public DeterministicNormalizationService(IEnumerable<IDeterministicNormalizationRule> rules)
    {
        _rules = rules.ToList();
    }

    public ServiceResult<NormalizationProposal> TryNormalize(NormalizationRequest request)
    {
        foreach (var rule in _rules)
        {
            if (rule.CanHandle(request))
                return ServiceResult<NormalizationProposal>.Ok(rule.Apply(request));
        }

        return ServiceResult<NormalizationProposal>.Fail(
            "No deterministic normalization rule matched the schema.");
    }
}
