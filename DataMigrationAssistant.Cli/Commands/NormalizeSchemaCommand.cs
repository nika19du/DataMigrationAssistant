using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Normalization;
using DataMigrationAssistant.Core.Results;
using DataMigrationAssistant.Core.Services;
using System.CommandLine;
using System.Net.Http;
using System.Text.Json;

namespace DataMigrationAssistant.Cli.Commands;

public static class NormalizeSchemaCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Command Build(
        IDataLoadService dataLoadService,
        ISchemaInferenceService schemaService,
        INormalizationServiceFactory normalizationFactory,
        INormalizationSqlGeneratorService sqlGeneratorService,
        IDeterministicNormalizationService deterministicService)
    {
        var command = new Command("normalize-schema",
            "AI-assisted schema normalization: detect logical entities in a flat Excel sheet and propose normalized tables");

        var fileArg = new Argument<FileInfo>("excel-file")
        {
            Description = "Path to the Excel (.xlsx) file to normalize"
        };
        fileArg.AcceptExistingOnly();

        var providerOption = new Option<string?>("--provider")
        {
            Description = "AI provider to use: claude, ollama, deterministic. Omit to see the error prompt for missing provider.",
        };

        var outputDirOption = new Option<DirectoryInfo?>("--output-dir")
        {
            Description = "Directory to write output files: normalization-report.md, normalized-schema.sql, normalized-seed.sql, normalization-proposal.json",
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

        command.Arguments.Add(fileArg);
        command.Options.Add(providerOption);
        command.Options.Add(outputDirOption);
        command.Options.Add(headerRowOption);
        command.Options.Add(sheetNameOption);
        command.Options.Add(sheetIndexOption);

        command.SetAction(async (ParseResult result) =>
        {
            var excelFile  = result.GetRequiredValue(fileArg);
            var provider   = result.GetValue(providerOption);
            var outputDir  = result.GetValue(outputDirOption);
            var headerRow  = result.GetValue(headerRowOption);
            var sheetName  = result.GetValue(sheetNameOption);
            var sheetIndex = result.GetValue(sheetIndexOption);

            // 1. Load and infer flat schema
            var loadResult = dataLoadService.LoadAllRows(excelFile.FullName, headerRow, sheetName, sheetIndex);
            if (!loadResult.Success)
            {
                WriteError(loadResult.Error!);
                return;
            }

            var data   = loadResult.Value!;
            var schema = schemaService.InferSchema(data);

            var request = new NormalizationRequest { SheetPreview = data, FlatSchema = schema };

            // 2. Resolve normalization proposal
            NormalizationProposal aiProposal;
            var providerLower = provider?.ToLowerInvariant();

            if (providerLower == "deterministic")
            {
                // Skip AI entirely — use rule engine directly
                Console.Error.WriteLine("Provider: deterministic");
                var detResult = deterministicService.TryNormalize(request);
                if (!detResult.Success)
                {
                    WriteError(detResult.Error!);
                    return;
                }
                aiProposal = detResult.Value!;
                Console.Error.WriteLine($"Deterministic proposal: {aiProposal.Tables.Count} table(s)");
            }
            else
            {
                // AI first; deterministic fallback only for real AI providers (not null/unknown)
                var isAiProvider = providerLower is "claude" or "ollama";
                var normService  = normalizationFactory.Create(provider);

                Console.Error.WriteLine($"Provider: {provider ?? "(none)"}");
                Console.Error.WriteLine("Sending to AI provider for normalization analysis...");

                ServiceResult<NormalizationProposal> proposeResult =
                    ServiceResult<NormalizationProposal>.Fail("Unknown error");
                try
                {
                    proposeResult = await normService.ProposeAsync(request);
                }
                catch (InvalidOperationException ex)
                {
                    proposeResult = ServiceResult<NormalizationProposal>.Fail(ex.Message);
                }
                catch (HttpRequestException ex)
                {
                    proposeResult = ServiceResult<NormalizationProposal>.Fail(
                        $"HTTP error communicating with AI provider: {ex.Message}");
                }
                catch (TaskCanceledException)
                {
                    proposeResult = ServiceResult<NormalizationProposal>.Fail("AI provider request timed out.");
                }
                catch (Exception ex)
                {
                    proposeResult = ServiceResult<NormalizationProposal>.Fail(
                        $"Unexpected error from AI provider: {ex.Message}");
                }

                var aiSucceeded = proposeResult.Success &&
                                  (proposeResult.Value?.Tables.Count ?? 0) > 0;

                if (!aiSucceeded && isAiProvider)
                {
                    Console.Error.WriteLine(
                        "AI normalization failed; using deterministic normalization fallback.");
                    var detResult = deterministicService.TryNormalize(request);
                    if (!detResult.Success)
                    {
                        WriteError(detResult.Error!);
                        return;
                    }
                    aiProposal = detResult.Value!;
                    Console.Error.WriteLine($"Deterministic proposal: {aiProposal.Tables.Count} table(s)");
                }
                else if (!aiSucceeded)
                {
                    // Null / unknown provider — preserve original error behaviour, no fallback
                    WriteError(proposeResult.Error ?? "No AI normalization provider configured.");
                    return;
                }
                else
                {
                    aiProposal = proposeResult.Value!;
                    Console.Error.WriteLine($"Proposal received: {aiProposal.Tables.Count} table(s)");
                }
            }

            // 3. Deterministic SQL generation from structural proposal
            var sqlResult = sqlGeneratorService.Generate(aiProposal, data);
            if (!sqlResult.Success)
            {
                WriteError(sqlResult.Error!);
                return;
            }

            var proposal = sqlResult.Value!;

            // 4. Console output
            PrintProposal(data.SheetName, proposal);

            // 5. Optional file output
            if (outputDir is not null)
            {
                try
                {
                    outputDir.Create();
                    WriteOutputFiles(outputDir.FullName, proposal);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Error.WriteLine($"Files written to {outputDir.FullName}");
                    Console.ResetColor();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    WriteError($"Could not write output files: {ex.Message}");
                }
            }
        });

        return command;
    }

    private static void PrintProposal(string sheetName, NormalizationProposal proposal)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Normalization Proposal ===");
        Console.ResetColor();
        Console.WriteLine($"Sheet : {sheetName}");
        Console.WriteLine();
        Console.WriteLine(proposal.MarkdownReport);

        if (proposal.Tables.Count == 0) return;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"=== Proposed {proposal.Tables.Count} Table(s) ===");
        Console.ResetColor();
        foreach (var t in proposal.Tables)
        {
            var fkCount = t.Columns.Count(c => c.ForeignKeyTo is not null);
            var fkNote  = fkCount > 0 ? $", {fkCount} FK" : string.Empty;
            Console.WriteLine($"  {t.TableName}  ({t.Columns.Count} cols{fkNote}, source: [{string.Join(", ", t.SourceColumns)}])");
        }
    }

    private static void WriteOutputFiles(string dir, NormalizationProposal proposal)
    {
        File.WriteAllText(Path.Combine(dir, "normalization-report.md"), proposal.MarkdownReport);
        File.WriteAllText(Path.Combine(dir, "normalized-schema.sql"),   proposal.CombinedMigrationSql);
        File.WriteAllText(Path.Combine(dir, "normalized-seed.sql"),     proposal.CombinedSeedSql);
        File.WriteAllText(Path.Combine(dir, "normalization-proposal.json"),
            JsonSerializer.Serialize(proposal, JsonOptions));
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Error: {message}");
        Console.ResetColor();
    }
}
