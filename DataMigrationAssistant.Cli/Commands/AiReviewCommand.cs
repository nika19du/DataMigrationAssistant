using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;
using System.CommandLine;

namespace DataMigrationAssistant.Cli.Commands;

public static class AiReviewCommand
{
    public static Command Build(
        ISqlSeedParserService seedParserService,
        IDataLoadService dataLoadService,
        ISchemaInferenceService schemaService,
        ISeedDiffService diffService,
        IMigrationSqlGeneratorService migrationService,
        IValidationService validationService,
        IAiReviewServiceFactory aiReviewFactory)
    {
        var command = new Command("ai-review", "AI-powered review of a migration");

        var sqlFileArg = new Argument<FileInfo>("old-seed-sql-file")
        {
            Description = "Path to the existing seed SQL file (.sql)"
        };
        sqlFileArg.AcceptExistingOnly();

        var excelFileArg = new Argument<FileInfo>("excel-file")
        {
            Description = "Path to the current Excel (.xlsx) file"
        };
        excelFileArg.AcceptExistingOnly();

        var providerOption = new Option<string?>("--provider")
        {
            Description = "AI provider to use: null (default), claude, ollama",
        };

        var headerRowOption = new Option<int>("--header-row")
        {
            Description = "1-based row number to use as the header row (0 = auto-detect, the default). Example: --header-row 2"
        };

        var sheetNameOption = new Option<string?>("--sheet")
        {
            Description = "Name of the worksheet to read. Example: --sheet \"Global PayGroup GTN Validation\""
        };

        var sheetIndexOption = new Option<int?>("--sheet-index")
        {
            Description = "1-based index of the worksheet to read. Example: --sheet-index 2"
        };

        command.Arguments.Add(sqlFileArg);
        command.Arguments.Add(excelFileArg);
        command.Options.Add(providerOption);
        command.Options.Add(headerRowOption);
        command.Options.Add(sheetNameOption);
        command.Options.Add(sheetIndexOption);

        command.SetAction(async (ParseResult result) =>
        {
            var sqlFile    = result.GetRequiredValue(sqlFileArg);
            var excelFile  = result.GetRequiredValue(excelFileArg);
            var provider   = result.GetValue(providerOption);
            var headerRow  = result.GetValue(headerRowOption);
            var sheetName  = result.GetValue(sheetNameOption);
            var sheetIndex = result.GetValue(sheetIndexOption);

            var aiReviewService = aiReviewFactory.Create(provider);

            var sql = File.ReadAllText(sqlFile.FullName);

            var seedResult = seedParserService.Parse(sql);
            if (!seedResult.Success)
            {
                WriteError(seedResult.Error!);
                return;
            }

            var loadResult = dataLoadService.LoadAllRows(excelFile.FullName, headerRow, sheetName, sheetIndex);
            if (!loadResult.Success)
            {
                WriteError(loadResult.Error!);
                return;
            }

            var data       = loadResult.Value!;
            var schema     = schemaService.InferSchema(data);
            var validation = validationService.Validate(data, schema);

            foreach (var w in validation.Warnings)
            {
                Console.ForegroundColor = w.Severity == ValidationSeverity.Warning
                    ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                Console.Error.WriteLine($"[{w.Severity.ToString().ToUpperInvariant()}] {w.Message}");
                Console.ResetColor();
            }

            if (!validation.CanProceed)
                return;

            var diffResult = diffService.Diff(seedResult.Value!, data, schema);
            if (!diffResult.Success)
            {
                WriteWarning(diffResult.Error!);
                return;
            }

            var migrationResult = migrationService.GenerateMigration(diffResult.Value!, schema);
            if (!migrationResult.Success)
            {
                WriteError(migrationResult.Error!);
                return;
            }

            var reviewRequest = new AiReviewRequest
            {
                SheetPreview     = data,
                TableSchema      = schema,
                ValidationResult = validation,
                SeedDiffResult   = diffResult.Value,
                MigrationSql     = migrationResult.Value,
            };

            Console.Error.WriteLine($"Sending to {provider ?? "null"} for review...");

            AiReviewResult review;
            try
            {
                review = await aiReviewService.ReviewAsync(reviewRequest);
            }
            catch (InvalidOperationException ex)
            {
                WriteError(ex.Message);
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== AI Review Summary ===");
            Console.ResetColor();
            Console.WriteLine(review.Summary);
            Console.WriteLine();

            if (review.Risks.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("=== Risks ===");
                Console.ResetColor();
                foreach (var risk in review.Risks)
                    Console.WriteLine($"[{risk.Level}] {risk.Description}");
                Console.WriteLine();
            }

            if (review.Recommendations.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("=== Recommendations ===");
                Console.ResetColor();
                foreach (var rec in review.Recommendations)
                {
                    Console.WriteLine($"[{rec.Priority}] {rec.Description}");
                    if (!string.IsNullOrWhiteSpace(rec.Action))
                        Console.WriteLine($"  Action: {rec.Action}");
                }
            }
        });

        return command;
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Error: {message}");
        Console.ResetColor();
    }

    private static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"Warning: {message}");
        Console.ResetColor();
    }
}
