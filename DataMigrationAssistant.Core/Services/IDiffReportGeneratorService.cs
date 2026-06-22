using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface IDiffReportGeneratorService
{
    string GenerateReport(SeedDiffResult diff);
}
