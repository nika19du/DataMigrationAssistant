namespace DataMigrationAssistant.Core.Services;

public interface IDataAnalysisServiceFactory
{
    IDataAnalysisService Create(string? provider);
}
