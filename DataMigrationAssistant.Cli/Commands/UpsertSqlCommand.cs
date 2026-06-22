using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;
using System.CommandLine;

namespace DataMigrationAssistant.Cli.Commands;

public static class UpsertSqlCommand
{
    public static Command Build(
        IDataLoadService dataLoadService,
        ISchemaInferenceService schemaService,
        IUpsertSqlGeneratorService upsertService,
        IValidationService validationService)
    {
        var command = new Command("upsert-sql", "Generate INSERT ... ON CONFLICT ... DO UPDATE SQL from an Excel file");

        var fileArg = new Argument<FileInfo>("excel-file") { Description = "Path to the Excel (.xlsx) file" };
        fileArg.AcceptExistingOnly();
        command.Arguments.Add(fileArg);

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

        command.Options.Add(headerRowOption);
        command.Options.Add(sheetNameOption);
        command.Options.Add(sheetIndexOption);

        command.SetAction((ParseResult result) =>
        {
            var file       = result.GetRequiredValue(fileArg);
            var headerRow  = result.GetValue(headerRowOption);
            var sheetName  = result.GetValue(sheetNameOption);
            var sheetIndex = result.GetValue(sheetIndexOption);

            var loadResult = dataLoadService.LoadAllRows(file.FullName, headerRow, sheetName, sheetIndex);
            if (!loadResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {loadResult.Error}");
                Console.ResetColor();
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

            var upsertResult = upsertService.GenerateUpsert(data, schema);

            if (!upsertResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"Warning: {upsertResult.Error}");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"-- Source  : {file.FullName}");
            Console.WriteLine($"-- Sheet   : {data.SheetName}");
            Console.WriteLine($"-- Table   : {schema.TableName}");
            Console.WriteLine($"-- Rows    : {data.Rows.Count}");
            Console.WriteLine();
            Console.WriteLine(upsertResult.Value);
        });

        return command;
    }
}
