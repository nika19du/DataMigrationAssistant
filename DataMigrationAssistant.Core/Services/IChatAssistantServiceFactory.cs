namespace DataMigrationAssistant.Core.Services;

public interface IChatAssistantServiceFactory
{
    IChatAssistantService Create(string? provider);
}
