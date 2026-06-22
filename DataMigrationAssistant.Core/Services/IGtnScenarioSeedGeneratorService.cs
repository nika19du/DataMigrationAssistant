using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface IGtnScenarioSeedGeneratorService
{
    GtnSeedGenerationResult Generate(SheetPreview sheetPreview);
}
