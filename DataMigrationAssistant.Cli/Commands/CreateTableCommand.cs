using DataMigrationAssistant.Core.Services;
using System.CommandLine;

namespace DataMigrationAssistant.Cli.Commands;

public static class CreateTableCommand
{
    public static Command Build(
        IPreviewService previewService,
        ISchemaInferenceService schemaService,
        ISqlGeneratorService sqlService)
    {
        var command = new Command("create-table", "Generate a CREATE TABLE SQL statement from an Excel file");

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

            var parseResult = previewService.GeneratePreview(file.FullName, headerRow, sheetName, sheetIndex);
            if (!parseResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {parseResult.Error}");
                Console.ResetColor();
                return;
            }

            var preview = parseResult.Value!;
            var schema  = schemaService.InferSchema(preview);
            var sql     = sqlService.GenerateCreateTable(schema);

            Console.WriteLine($"-- Source  : {file.FullName}");
            Console.WriteLine($"-- Sheet   : {preview.SheetName}");
            Console.WriteLine($"-- Table   : {schema.TableName}");
            Console.WriteLine($"-- Columns : {schema.Columns.Count}");
            Console.WriteLine($"-- Sample  : {schema.SampleRowCount} rows");
            Console.WriteLine();
            Console.WriteLine(sql);
        });

        return command;
    }
}
