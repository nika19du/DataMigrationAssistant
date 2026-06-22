using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface IWarningReportGeneratorService
{
    string GenerateGtnWarningReport(IReadOnlyList<GtnSeedWarning> warnings, int scenarioCount);
}
