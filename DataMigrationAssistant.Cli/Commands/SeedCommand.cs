using DataMigrationAssistant.Core.Services;
using System.CommandLine;

namespace DataMigrationAssistant.Cli.Commands;

public static class SeedCommand
{
    public static Command Build(IPreviewService previewService)
    {
        var command = new Command("seed", "Preview the first rows of an Excel file before generating a seed script");

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

            var serviceResult = previewService.GeneratePreview(file.FullName, headerRow, sheetName, sheetIndex);

            if (!serviceResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {serviceResult.Error}");
                Console.ResetColor();
                return;
            }

            var preview = serviceResult.Value!;

            Console.WriteLine($"Sheet    : {preview.SheetName}");
            Console.WriteLine($"File     : {preview.FilePath}");
            Console.WriteLine($"Columns  : {preview.Columns.Count}");
            Console.WriteLine($"Rows     : {preview.TotalRowCount} total (showing first {preview.Rows.Count})");
            Console.WriteLine();

            var colWidths = preview.Columns
                .Select(c => Math.Max(c.SnakeCaseName.Length, 12))
                .ToList();

            var header  = string.Join(" | ", preview.Columns.Select((c, i) => c.SnakeCaseName.PadRight(colWidths[i])));
            var divider = string.Join("-+-", colWidths.Select(w => new string('-', w)));

            Console.WriteLine(header);
            Console.WriteLine(divider);

            foreach (var row in preview.Rows)
            {
                var line = string.Join(" | ", preview.Columns.Select((c, i) =>
                    (row.TryGetValue(c.SnakeCaseName, out var v) ? v ?? "NULL" : "NULL").PadRight(colWidths[i])));
                Console.WriteLine(line);
            }
        });

        return command;
    }
}
