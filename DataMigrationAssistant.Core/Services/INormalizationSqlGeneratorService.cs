using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public interface INormalizationSqlGeneratorService
{
    ServiceResult<NormalizationProposal> Generate(
        NormalizationProposal proposal,
        SheetPreview sourceData);
}
