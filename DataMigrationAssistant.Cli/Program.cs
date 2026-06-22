using DataMigrationAssistant.Cli.Commands;
using DataMigrationAssistant.Core.Extensions;
using DataMigrationAssistant.Core.Normalization;
using DataMigrationAssistant.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;


var services = new ServiceCollection();
services.AddDataMigrationCore();
var provider = services.BuildServiceProvider();

var previewService    = provider.GetRequiredService<IPreviewService>();
var dataLoadService   = provider.GetRequiredService<IDataLoadService>();
var schemaService     = provider.GetRequiredService<ISchemaInferenceService>();
var sqlService        = provider.GetRequiredService<ISqlGeneratorService>();
var seedSqlService    = provider.GetRequiredService<ISeedSqlGeneratorService>();
var upsertService     = provider.GetRequiredService<IUpsertSqlGeneratorService>();
var seedParserService = provider.GetRequiredService<ISqlSeedParserService>();
var diffService       = provider.GetRequiredService<ISeedDiffService>();
var reportService     = provider.GetRequiredService<IDiffReportGeneratorService>();
var migrationService   = provider.GetRequiredService<IMigrationSqlGeneratorService>();
var validationService  = provider.GetRequiredService<IValidationService>();
var aiReviewFactory        = provider.GetRequiredService<IAiReviewServiceFactory>();
var normalizationFactory     = provider.GetRequiredService<INormalizationServiceFactory>();
var normalizationSqlService  = provider.GetRequiredService<INormalizationSqlGeneratorService>();
var deterministicNormService = provider.GetRequiredService<IDeterministicNormalizationService>();
var gtnSeedService           = provider.GetRequiredService<IGtnScenarioSeedGeneratorService>();

var root = new RootCommand("Data Migration Assistant — generate PostgreSQL seed and migration scripts from Excel/CSV files");
root.Subcommands.Add(SeedCommand.Build(previewService));
root.Subcommands.Add(CreateTableCommand.Build(previewService, schemaService, sqlService));
root.Subcommands.Add(SeedSqlCommand.Build(dataLoadService, schemaService, seedSqlService, validationService));
root.Subcommands.Add(UpsertSqlCommand.Build(dataLoadService, schemaService, upsertService, validationService));
root.Subcommands.Add(DiffCommand.Build(seedParserService, dataLoadService, schemaService, diffService, reportService));
root.Subcommands.Add(GenerateMigrationCommand.Build(seedParserService, dataLoadService, schemaService, diffService, migrationService, validationService));
root.Subcommands.Add(AiReviewCommand.Build(seedParserService, dataLoadService, schemaService, diffService, migrationService, validationService, aiReviewFactory));
root.Subcommands.Add(NormalizeSchemaCommand.Build(dataLoadService, schemaService, normalizationFactory, normalizationSqlService, deterministicNormService));
root.Subcommands.Add(GenerateGtnScenariosCommand.Build(dataLoadService, gtnSeedService));

var parseResult = root.Parse(args);
return await parseResult.InvokeAsync(new InvocationConfiguration(), default);
