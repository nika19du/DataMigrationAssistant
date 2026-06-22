using DataMigrationAssistant.Core.Agents;
using DataMigrationAssistant.Core.Normalization;
using DataMigrationAssistant.Core.Parsers;
using DataMigrationAssistant.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DataMigrationAssistant.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMigrationAgents(this IServiceCollection services)
    {
        services.AddSingleton<GtnAgent>();
        services.AddSingleton<SchemaAgent>();
        services.AddSingleton<ValidationAgent>();
        services.AddSingleton<DataAnalysisAgent>();
        services.AddSingleton<NormalizationAgent>();
        services.AddSingleton<SqlGenerationAgent>();
        services.AddSingleton<GeneralMigrationAgent>();
        services.AddSingleton<IMigrationAgentRouter, MigrationAgentRouter>();
        services.AddSingleton<IMigrationAssistant, MigrationAssistant>();
        return services;
    }

    public static IServiceCollection AddDataMigrationCore(this IServiceCollection services)
    {
        services.AddSingleton<IExcelParser, ExcelParser>();
        services.AddSingleton<IPreviewService, PreviewService>();
        services.AddSingleton<IDataLoadService, DataLoadService>();
        services.AddSingleton<ISchemaInferenceService, SchemaInferenceService>();
        services.AddSingleton<ISqlGeneratorService, SqlGeneratorService>();
        services.AddSingleton<ISeedSqlGeneratorService, SeedSqlGeneratorService>();
        services.AddSingleton<IUpsertSqlGeneratorService, UpsertSqlGeneratorService>();
        services.AddSingleton<ISqlSeedParserService, SqlSeedParserService>();
        services.AddSingleton<ISeedDiffService, SeedDiffService>();
        services.AddSingleton<IDiffReportGeneratorService, DiffReportGeneratorService>();
        services.AddSingleton<IMigrationSqlGeneratorService, MigrationSqlGeneratorService>();
        services.AddSingleton<IValidationService, ValidationService>();

        // AI Review providers — select at runtime via IAiReviewServiceFactory
        services.AddSingleton<NullAiReviewService>();
        services.AddSingleton<ClaudeAiReviewService>();
        services.AddSingleton(_ => new OllamaAiReviewService(new HttpClient { BaseAddress = new Uri("http://localhost:11434") }));
        services.AddSingleton<IAiReviewServiceFactory, AiReviewServiceFactory>();
        services.AddSingleton<IAiReviewService>(sp => sp.GetRequiredService<NullAiReviewService>());

        // Normalization providers — select at runtime via INormalizationServiceFactory
        services.AddSingleton<NullNormalizationService>();
        services.AddSingleton<ClaudeNormalizationService>();
        services.AddSingleton(_ => new OllamaNormalizationService(
            new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) }));
        services.AddSingleton<INormalizationServiceFactory, NormalizationServiceFactory>();
        services.AddSingleton<INormalizationProposalService>(sp => sp.GetRequiredService<NullNormalizationService>());
        services.AddSingleton<INormalizationSqlGeneratorService, NormalizationSqlGeneratorService>();

        // Deterministic normalization rules — registered in priority order; first match wins
        services.AddSingleton<IDeterministicNormalizationRule, ValidationScenarioNormalizationRule>();
        services.AddSingleton<IDeterministicNormalizationRule, GenericNormalizationRule>();
        services.AddSingleton<IDeterministicNormalizationService, DeterministicNormalizationService>();

        services.AddSingleton<IGtnScenarioSeedGeneratorService, GtnScenarioSeedGeneratorService>();
        services.AddSingleton<IWarningReportGeneratorService, WarningReportGeneratorService>();

        // Data Analysis providers — select at runtime via IDataAnalysisServiceFactory
        services.AddSingleton<DeterministicDataAnalysisService>();
        services.AddSingleton<ClaudeDataAnalysisService>();
        services.AddSingleton(_ => new OllamaDataAnalysisService(
            new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) }));
        services.AddSingleton<IDataAnalysisServiceFactory, DataAnalysisServiceFactory>();
        services.AddSingleton<IDataAnalysisService>(sp =>
            sp.GetRequiredService<DeterministicDataAnalysisService>());

        // Chat Assistant providers — select at runtime via IChatAssistantServiceFactory
        services.AddSingleton<NullChatAssistantService>();
        services.AddSingleton<ClaudeChatAssistantService>();
        services.AddSingleton(_ => new OllamaChatAssistantService(
            new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) }));
        services.AddSingleton<IChatAssistantServiceFactory, ChatAssistantServiceFactory>();

        return services;
    }
}
