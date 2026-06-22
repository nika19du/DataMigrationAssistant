namespace DataMigrationAssistant.Core.Services;

public interface IAiReviewServiceFactory
{
    IAiReviewService Create(string? provider);
}
