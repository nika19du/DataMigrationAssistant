using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;
using System.CommandLine;
using System.Text;

namespace DataMigrationAssistant.Cli.Commands;

public static class GenerateGtnScenariosCommand
{
    public static Command Build(
        IDataLoadService dataLoadService,
        IGtnScenarioSeedGeneratorService gtnService)
    {
        var command = new Command(
            "generate-gtn-scenarios",
            "Generate PostgreSQL seed scripts for GTN validation scenarios from an Excel file");

        var fileArg = new Argument<FileInfo>("excel-file")
        {
            Description = "Path to the Excel (.xlsx) file containing GTN validation scenarios"
        };
        fileArg.AcceptExistingOnly();
        command.Arguments.Add(fileArg);

        var sheetOption = new Option<string?>("--sheet")
        {
            Description = "Name of the worksheet to read (default: first sheet). Example: --sheet \"Global PayGroup GTN Validation\""
        };

        var headerRowOption = new Option<int>("--header-row")
        {
            Description = "1-based row number of the header row (0 = auto-detect). Example: --header-row 2"
        };

        var outputDirOption = new Option<DirectoryInfo?>("--output-dir")
        {
            Description = "Directory where gtn-scenarios-seed.sql and gtn-seed-warnings.md will be written"
        };

        command.Options.Add(sheetOption);
        command.Options.Add(headerRowOption);
        command.Options.Add(outputDirOption);

        command.SetAction((ParseResult result) =>
        {
            var file      = result.GetRequiredValue(fileArg);
            var sheet     = result.GetValue(sheetOption);
            var headerRow = result.GetValue(headerRowOption);
            var outputDir = result.GetValue(outputDirOption);

            if (outputDir is null)
            {
                WriteError("--output-dir is required.");
                return;
            }

            var loadResult = dataLoadService.LoadAllRows(file.FullName, headerRow, sheet);
            if (!loadResult.Success)
            {
                WriteError(loadResult.Error!);
                return;
            }

            var preview = loadResult.Value!;
            var genResult = gtnService.Generate(preview);

            try
            {
                outputDir.Create();
                var sqlPath      = Path.Combine(outputDir.FullName, "gtn-scenarios-seed.sql");
                var warningsPath = Path.Combine(outputDir.FullName, "gtn-seed-warnings.md");

                File.WriteAllText(sqlPath, genResult.ScenariosSql, Encoding.UTF8);
                File.WriteAllText(warningsPath, BuildWarningsMarkdown(genResult.Warnings), Encoding.UTF8);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Written: {sqlPath}");
                Console.WriteLine($"Written: {warningsPath}");
                Console.ResetColor();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WriteError($"Could not write output files: {ex.Message}");
                return;
            }

            var scenarioCount = CountInserts(genResult.ScenariosSql);
            Console.WriteLine($"Scenarios : {scenarioCount}");
            Console.WriteLine($"Warnings  : {genResult.Warnings.Count}");

            if (genResult.Warnings.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                foreach (var w in genResult.Warnings)
                    Console.Error.WriteLine($"[WARN] Row {w.RowNumber} ({w.ScenarioId}) — {w.Column}: {w.Message}");
                Console.ResetColor();
            }
        });

        return command;
    }

    private static string BuildWarningsMarkdown(IReadOnlyList<GtnSeedWarning> warnings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# GTN Seed Generation Warnings");
        sb.AppendLine();

        if (warnings.Count == 0)
        {
            sb.AppendLine("No warnings — all values mapped successfully.");
            return sb.ToString();
        }

        sb.AppendLine($"Total: {warnings.Count} warning(s)");
        sb.AppendLine();
        sb.AppendLine("| Row | Scenario ID | Column | Value | Message |");
        sb.AppendLine("|-----|-------------|--------|-------|---------|");

        foreach (var w in warnings)
        {
            var scenarioId = Md(w.ScenarioId ?? "—");
            var value      = Md(w.Value ?? "_(empty)_");
            sb.AppendLine($"| {w.RowNumber} | {scenarioId} | {w.Column} | {value} | {Md(w.Message)} |");
        }

        return sb.ToString();
    }

    private static string Md(string s) => s.Replace("|", "\\|");

    private static int CountInserts(string sql) =>
        (sql.Length - sql.Replace("INSERT INTO nomenclature.gtn_scenarios", string.Empty, StringComparison.Ordinal).Length)
        / "INSERT INTO nomenclature.gtn_scenarios".Length;

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Error: {message}");
        Console.ResetColor();
    }
}
