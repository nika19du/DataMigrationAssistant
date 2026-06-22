using DataMigrationAssistant.Core.Services;
using System.CommandLine;

namespace DataMigrationAssistant.Cli.Commands;

public static class DiffCommand
{
    public static Command Build(
        ISqlSeedParserService seedParserService,
        IDataLoadService dataLoadService,
        ISchemaInferenceService schemaService,
        ISeedDiffService diffService,
        IDiffReportGeneratorService reportService)
    {
        var command = new Command("diff", "Compare an existing seed SQL file against current Excel data");

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

        command.Arguments.Add(sqlFileArg);
        command.Arguments.Add(excelFileArg);

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
            var sqlFile    = result.GetRequiredValue(sqlFileArg);
            var excelFile  = result.GetRequiredValue(excelFileArg);
            var headerRow  = result.GetValue(headerRowOption);
            var sheetName  = result.GetValue(sheetNameOption);
            var sheetIndex = result.GetValue(sheetIndexOption);

            var sql = File.ReadAllText(sqlFile.FullName);

            var seedResult = seedParserService.Parse(sql);
            if (!seedResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {seedResult.Error}");
                Console.ResetColor();
                return;
            }

            var loadResult = dataLoadService.LoadAllRows(excelFile.FullName, headerRow, sheetName, sheetIndex);
            if (!loadResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {loadResult.Error}");
                Console.ResetColor();
                return;
            }

            var schema     = schemaService.InferSchema(loadResult.Value!);
            var diffResult = diffService.Diff(seedResult.Value!, loadResult.Value!, schema);
            if (!diffResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"Warning: {diffResult.Error}");
                Console.ResetColor();
                return;
            }

            Console.WriteLine(reportService.GenerateReport(diffResult.Value!));
        });

        return command;
    }
}
