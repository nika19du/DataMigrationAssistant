namespace DataMigrationAssistant.Core.Services;

public interface INormalizationServiceFactory
{
    INormalizationProposalService Create(string? provider);
}
