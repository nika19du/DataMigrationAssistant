using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

// ── Empty context ─────────────────────────────────────────────────────────────

public class ChatContextBuilderEmptyTests
{
    [Fact]
    public void BuildSystemPrompt_EmptyContext_ContainsPreamble()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Migration Chat Assistant", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EmptyContext_InstructsDataOnly()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("data only", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_EmptyContext_InstructsDownloadCenter()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Download Center", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EmptyContext_InstructsNotToInventSchema()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Do not invent", prompt, StringComparison.OrdinalIgnoreCase);
    }
}

// ── Preview ───────────────────────────────────────────────────────────────────

public class ChatContextBuilderPreviewTests
{
    private static SheetPreview MakePreview(int rowCount, string sheetName = "Sheet1") =>
        new()
        {
            SheetName     = sheetName,
            FilePath      = "test.xlsx",
            TotalRowCount = rowCount,
            Columns       = [new ColumnInfo { Index = 0, Name = "id", SnakeCaseName = "id" }],
            Rows          = Enumerable.Range(1, rowCount)
                .Select(i => (IReadOnlyDictionary<string, string?>)
                    new Dictionary<string, string?> { ["id"] = i.ToString() })
                .ToList(),
        };

    [Fact]
    public void BuildSystemPrompt_WithPreview_IncludesSheetName()
    {
        var ctx    = new ChatContext { Preview = MakePreview(10, "Employees") };
        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("Employees", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithPreview_IncludesTotalRowCount()
    {
        var ctx    = new ChatContext { Preview = MakePreview(42) };
        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("42", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithPreview_CapsAtMaxPreviewRows()
    {
        var ctx    = new ChatContext { Preview = MakePreview(20) };
        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        // MaxPreviewRows = 5 — Row 6 must not appear
        Assert.Contains($"Row {ChatContextBuilder.MaxPreviewRows}:", prompt);
        Assert.DoesNotContain($"Row {ChatContextBuilder.MaxPreviewRows + 1}:", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithPreview_NullCellRenderedAsNULL()
    {
        var preview = new SheetPreview
        {
            SheetName     = "Sheet1",
            FilePath      = "f.xlsx",
            TotalRowCount = 1,
            Columns       = [new ColumnInfo { Index = 0, Name = "name", SnakeCaseName = "name" }],
            Rows          = [new Dictionary<string, string?> { ["name"] = null }],
        };

        var ctx    = new ChatContext { Preview = preview };
        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("name=NULL", prompt);
    }
}

// ── Schema ────────────────────────────────────────────────────────────────────

public class ChatContextBuilderSchemaTests
{
    [Fact]
    public void BuildSystemPrompt_WithSchema_IncludesTableName()
    {
        var ctx = new ChatContext
        {
            Schema = new TableSchema { TableName = "employees", SheetName = "Sheet1" },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("employees", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithSchema_IncludesColumnNames()
    {
        var ctx = new ChatContext
        {
            Schema = new TableSchema
            {
                TableName = "t",
                Columns   =
                [
                    new ColumnSchema { SnakeCaseName = "employee_id", InferredType = PostgresType.Integer },
                    new ColumnSchema { SnakeCaseName = "hire_date",   InferredType = PostgresType.Date },
                ],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("employee_id", prompt);
        Assert.Contains("hire_date", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_CandidateKeyColumn_MarkedInPrompt()
    {
        var ctx = new ChatContext
        {
            Schema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "id", IsCandidateKey = true }],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("candidate key", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ColumnWithDuplicates_MarkedInPrompt()
    {
        var ctx = new ChatContext
        {
            Schema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "status", HasDuplicates = true }],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("has duplicates", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullableColumn_MarkedAsNull()
    {
        var ctx = new ChatContext
        {
            Schema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "notes", IsNullable = true }],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("NULL", prompt);
        Assert.DoesNotContain("NOT NULL", prompt.Split('\n')
            .First(l => l.Contains("notes")));
    }
}

// ── Validation ────────────────────────────────────────────────────────────────

public class ChatContextBuilderValidationTests
{
    [Fact]
    public void BuildSystemPrompt_WithWarnings_IncludesWarningMessages()
    {
        var ctx = new ChatContext
        {
            Validation = new ValidationResult
            {
                Warnings = [new ValidationWarning
                {
                    Code     = "NO_CANDIDATE_KEY",
                    Severity = ValidationSeverity.Warning,
                    Message  = "No unique column found.",
                }],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("No unique column found.", prompt);
        Assert.Contains("NO_CANDIDATE_KEY", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithColumnWarning_IncludesColumnName()
    {
        var ctx = new ChatContext
        {
            Validation = new ValidationResult
            {
                Warnings = [new ValidationWarning
                {
                    Code       = "TYPE_CONFLICT",
                    Severity   = ValidationSeverity.Warning,
                    Message    = "Mixed types detected.",
                    ColumnName = "amount",
                }],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("amount", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NoWarnings_SaysClean()
    {
        var ctx = new ChatContext
        {
            Validation = new ValidationResult(),
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("No warnings", prompt, StringComparison.OrdinalIgnoreCase);
    }
}

// ── Data Analysis ─────────────────────────────────────────────────────────────

public class ChatContextBuilderAnalysisTests
{
    [Fact]
    public void BuildSystemPrompt_WithAnalysis_IncludesSummary()
    {
        var ctx = new ChatContext
        {
            AnalysisResult = new DataAnalysisResult
            {
                Summary = "Dataset has 5 columns and looks clean.",
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("Dataset has 5 columns", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithFindings_IncludesTopFindings()
    {
        var findings = Enumerable.Range(1, 10)
            .Select(i => new DataAnalysisFinding
            {
                Category    = "CandidateKey",
                Severity    = "INFO",
                Description = $"Finding {i}",
            })
            .ToList();

        var ctx = new ChatContext
        {
            AnalysisResult = new DataAnalysisResult
            {
                Summary  = "s",
                Findings = findings,
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        // Should include only first MaxAnalysisFindings
        Assert.Contains("Finding 1", prompt);
        Assert.Contains("Finding 3", prompt);
        Assert.DoesNotContain($"Finding {ChatContextBuilder.MaxAnalysisFindings + 1}", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EmptyAnalysisSummary_EmitsAnalysisNotRun()
    {
        var ctx = new ChatContext
        {
            AnalysisResult = new DataAnalysisResult { Summary = string.Empty },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("<data_analysis>", prompt);
        Assert.Contains("ANALYSIS_NOT_RUN", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithRecommendations_IncludesThem()
    {
        var ctx = new ChatContext
        {
            AnalysisResult = new DataAnalysisResult
            {
                Summary         = "ok",
                Recommendations =
                [
                    new DataAnalysisRecommendation
                    {
                        Priority    = "HIGH",
                        Type        = "PrimaryKey",
                        Description = "Use id as primary key.",
                    },
                ],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("Use id as primary key.", prompt);
    }
}

// ── Normalization Proposal ────────────────────────────────────────────────────

public class ChatContextBuilderNormalizationTests
{
    [Fact]
    public void BuildSystemPrompt_WithProposal_IncludesReasoning()
    {
        var ctx = new ChatContext
        {
            NormalizationProposal = new NormalizationProposal
            {
                Reasoning = "Two distinct entities: scenarios and settings.",
                Tables    =
                [
                    new ProposedTable { TableName = "gtn_scenarios",        Columns = [] },
                    new ProposedTable { TableName = "gtn_scenario_settings", Columns = [] },
                ],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("Two distinct entities", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithProposal_IncludesTableNames()
    {
        var ctx = new ChatContext
        {
            NormalizationProposal = new NormalizationProposal
            {
                Tables =
                [
                    new ProposedTable { TableName = "employees", Columns = [] },
                    new ProposedTable { TableName = "departments", Columns = [] },
                ],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("employees", prompt);
        Assert.Contains("departments", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NoTables_EmitsNormalizationNotRun()
    {
        var ctx = new ChatContext
        {
            NormalizationProposal = new NormalizationProposal { Tables = [] },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("<normalization_status>", prompt);
        Assert.Contains("NORMALIZATION_NOT_RUN", prompt);
        Assert.DoesNotContain("<normalization_proposal>", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_LongReasoning_TruncatedAtMaxChars()
    {
        var longReasoning = new string('x', ChatContextBuilder.MaxNormReasoningChars + 100);
        var ctx = new ChatContext
        {
            NormalizationProposal = new NormalizationProposal
            {
                Reasoning = longReasoning,
                Tables    = [new ProposedTable { TableName = "t", Columns = [] }],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("…", prompt);
        Assert.DoesNotContain(longReasoning, prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithForeignKey_IncludesFkInTableLine()
    {
        var ctx = new ChatContext
        {
            NormalizationProposal = new NormalizationProposal
            {
                Tables =
                [
                    new ProposedTable
                    {
                        TableName = "settings",
                        Columns   =
                        [
                            new ProposedColumn { Name = "scenario_id", ForeignKeyTo = "scenarios(id)" },
                        ],
                    },
                ],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("scenarios(id)", prompt);
    }
}

// ── Product awareness ─────────────────────────────────────────────────────────

public class ChatContextBuilderProductAwarenessTests
{
    [Fact]
    public void BuildSystemPrompt_ContainsDataMigrationAssistantIdentity()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("DataMigrationAssistant", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_PrefersInAppWorkflow()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("in-app workflow", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_DiscouragesCsvManualPsqlAsPrimaryPath()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("psql", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("primary migration path", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsAppCapabilitiesSection()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("<app_capabilities>", prompt);
        Assert.Contains("Downloads tab", prompt);
        Assert.Contains("Normalization tab", prompt);
        Assert.Contains("Data Analysis tab", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_InstructsNotToInventUnavailableOutputs()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Do not invent unavailable files", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_EmptyContext_WorkflowStatusIndicatesUploadRequired()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("<workflow_status>", prompt);
        Assert.Contains("NOT YET", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EmptyContext_DataAnalysisShownAsNotYetRun()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("NOT YET RUN", prompt);
        Assert.Contains("Data Analysis tab", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithPreview_WorkflowStatusShowsPreviewComplete()
    {
        var ctx = new ChatContext
        {
            Preview = new SheetPreview
            {
                SheetName     = "Employees",
                FilePath      = "emp.xlsx",
                TotalRowCount = 50,
                Columns       = [],
                Rows          = [],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("Employees", prompt);
        Assert.Contains("50 rows", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithAnalysis_WorkflowStatusShowsAnalysisComplete()
    {
        var ctx = new ChatContext
        {
            AnalysisResult = new DataAnalysisResult { Summary = "Looks good." },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.DoesNotContain("Data Analysis:    NOT YET RUN", prompt);
        Assert.Contains("Data Analysis:    complete", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithNormalization_WorkflowStatusShowsNormalizationComplete()
    {
        var ctx = new ChatContext
        {
            NormalizationProposal = new NormalizationProposal
            {
                Tables = [new ProposedTable { TableName = "t", Columns = [] }],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.DoesNotContain("Normalization:    NOT YET RUN", prompt);
        Assert.Contains("Normalization:    complete", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsWorkflowStepsWithTabNames()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Preview tab", prompt);
        Assert.Contains("Schema tab", prompt);
        Assert.Contains("Validation tab", prompt);
    }
}

// ── Grounding rules ───────────────────────────────────────────────────────────

public class ChatContextBuilderGroundingRulesTests
{
    [Fact]
    public void BuildSystemPrompt_RulesIncludeNormalizationGroundingRule()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Never explain a normalization proposal that is not present in context", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_RulesIncludeNormalizationNotRunInstruction()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Run Normalize first", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_RulesIncludeSchemaGroundingRule()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Never describe schema columns or types that are not listed in <inferred_schema>", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_RulesIncludeValidationGroundingRule()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Never describe validation warnings that are not listed in <validation_results>", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_RulesIncludeAnalysisGroundingRule()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Never describe analysis findings that are not listed in <data_analysis>", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_RulesInstructNotToExposeInternalMarkers()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("grounding tokens for your reasoning only", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never quote them verbatim in user-facing answers", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_RulesInstructToTranslateMarkersIntoNaturalLanguage()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Translate internal markers into natural language", prompt, StringComparison.OrdinalIgnoreCase);
    }
}

// ── NOT_RUN sentinels ─────────────────────────────────────────────────────────

public class ChatContextBuilderNotRunSentinelTests
{
    [Fact]
    public void BuildSystemPrompt_NullNormalization_ContainsNormalizationNotRun()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("NORMALIZATION_NOT_RUN", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullNormalization_DoesNotContainNormalizationProposalTag()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.DoesNotContain("<normalization_proposal>", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullNormalization_ContainsNormalizationStatusTag()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("<normalization_status>", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullSchema_ContainsSchemaNotInferred()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("<inferred_schema>", prompt);
        Assert.Contains("SCHEMA_NOT_INFERRED", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullValidation_ContainsValidationNotRun()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("<validation_results>", prompt);
        Assert.Contains("VALIDATION_NOT_RUN", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullAnalysis_ContainsAnalysisNotRun()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("<data_analysis>", prompt);
        Assert.Contains("ANALYSIS_NOT_RUN", prompt);
    }

    [Fact]
    public void BuildSchemaSection_WithSchema_DoesNotContainSchemaNotInferred()
    {
        var section = ChatContextBuilder.BuildSchemaSection(
            new TableSchema { TableName = "t", Columns = [] });

        Assert.DoesNotContain("SCHEMA_NOT_INFERRED", section);
        Assert.Contains("Table: t", section);
    }

    [Fact]
    public void BuildValidationSection_WithValidation_DoesNotContainValidationNotRun()
    {
        var section = ChatContextBuilder.BuildValidationSection(new ValidationResult());

        Assert.DoesNotContain("VALIDATION_NOT_RUN", section);
        Assert.Contains("No warnings", section);
    }

    [Fact]
    public void BuildAnalysisSection_WithAnalysis_DoesNotContainAnalysisNotRun()
    {
        var section = ChatContextBuilder.BuildAnalysisSection(
            new DataAnalysisResult { Summary = "Looks clean." });

        Assert.DoesNotContain("ANALYSIS_NOT_RUN", section);
        Assert.Contains("Looks clean.", section);
    }
}

// ── Primary key recommendation ────────────────────────────────────────────────

public class ChatContextBuilderPrimaryKeyRecommendationTests
{
    private static TableSchema MakeSchema(params (string name, bool isCandidate, int index)[] columns) =>
        new()
        {
            TableName = "users",
            Columns   = columns.Select(c => new ColumnSchema
            {
                Index          = c.index,
                SnakeCaseName  = c.name,
                IsCandidateKey = c.isCandidate,
            }).ToList(),
        };

    private static ValidationResult MakeMultipleCandidateKeyValidation(string selected, params string[] others)
    {
        var allNames = string.Join(", ", new[] { selected }.Concat(others));
        return new ValidationResult
        {
            Warnings =
            [
                new ValidationWarning
                {
                    Code     = "MULTIPLE_CANDIDATE_KEYS",
                    Severity = ValidationSeverity.Info,
                    Message  = $"Multiple candidate key columns found in 'users': {allNames}. The first ({selected}) will be used.",
                },
            ],
        };
    }

    [Fact]
    public void BuildPrimaryKeyRecommendationSection_AlwaysEmitsTags()
    {
        var section = ChatContextBuilder.BuildPrimaryKeyRecommendationSection(null, null);

        Assert.Contains("<primary_key_recommendation>", section);
        Assert.Contains("</primary_key_recommendation>", section);
    }

    [Fact]
    public void BuildPrimaryKeyRecommendationSection_MultipleCandidateKeys_ExtractsRecommendedId()
    {
        var schema     = MakeSchema(("id", true, 0), ("username", true, 1), ("score", true, 2));
        var validation = MakeMultipleCandidateKeyValidation("id", "username", "score");

        var section = ChatContextBuilder.BuildPrimaryKeyRecommendationSection(schema, validation);

        Assert.Contains("RECOMMENDED_PRIMARY_KEY: id", section);
    }

    [Fact]
    public void BuildPrimaryKeyRecommendationSection_MultipleCandidateKeys_OtherKeysContainUsernameAndScore()
    {
        var schema     = MakeSchema(("id", true, 0), ("username", true, 1), ("score", true, 2));
        var validation = MakeMultipleCandidateKeyValidation("id", "username", "score");

        var section = ChatContextBuilder.BuildPrimaryKeyRecommendationSection(schema, validation);

        Assert.Contains("OTHER_CANDIDATE_KEYS:", section);
        Assert.Contains("username", section);
        Assert.Contains("score", section);
    }

    [Fact]
    public void BuildPrimaryKeyRecommendationSection_MultipleCandidateKeys_BasisMentionsMultipleCandidateKeys()
    {
        var schema     = MakeSchema(("id", true, 0), ("username", true, 1));
        var validation = MakeMultipleCandidateKeyValidation("id", "username");

        var section = ChatContextBuilder.BuildPrimaryKeyRecommendationSection(schema, validation);

        Assert.Contains("MULTIPLE_CANDIDATE_KEYS", section);
    }

    [Fact]
    public void BuildPrimaryKeyRecommendationSection_SingleCandidateKey_RecommendsThatKey()
    {
        var schema     = MakeSchema(("id", true, 0), ("name", false, 1));
        var validation = new ValidationResult();

        var section = ChatContextBuilder.BuildPrimaryKeyRecommendationSection(schema, validation);

        Assert.Contains("RECOMMENDED_PRIMARY_KEY: id", section);
        Assert.DoesNotContain("OTHER_CANDIDATE_KEYS:", section);
    }

    [Fact]
    public void BuildPrimaryKeyRecommendationSection_SingleCandidateKey_BasisMentionsSingleCandidate()
    {
        var schema     = MakeSchema(("id", true, 0));
        var validation = new ValidationResult();

        var section = ChatContextBuilder.BuildPrimaryKeyRecommendationSection(schema, validation);

        Assert.Contains("Single candidate key", section);
    }

    [Fact]
    public void BuildPrimaryKeyRecommendationSection_NoCandidateKey_EmitsNoRecommendedPrimaryKey()
    {
        var schema     = MakeSchema(("name", false, 0), ("score", false, 1));
        var validation = new ValidationResult();

        var section = ChatContextBuilder.BuildPrimaryKeyRecommendationSection(schema, validation);

        Assert.Contains("NO_RECOMMENDED_PRIMARY_KEY", section);
    }

    [Fact]
    public void BuildPrimaryKeyRecommendationSection_NullSchema_EmitsNoRecommendedPrimaryKey()
    {
        var section = ChatContextBuilder.BuildPrimaryKeyRecommendationSection(null, null);

        Assert.Contains("NO_RECOMMENDED_PRIMARY_KEY", section);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsPrimaryKeyRecommendationSection()
    {
        var ctx = new ChatContext
        {
            Schema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "id", IsCandidateKey = true }],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("<primary_key_recommendation>", prompt);
        Assert.Contains("</primary_key_recommendation>", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EmptyContext_StillContainsPrimaryKeyRecommendationSection()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("<primary_key_recommendation>", prompt);
    }
}

// ── Candidate key grounding rules ─────────────────────────────────────────────

public class ChatContextBuilderCandidateKeyRulesTests
{
    [Fact]
    public void BuildSystemPrompt_RulesContainCandidateKeyDefinition()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("sample-unique and non-null", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_RulesContainMultipleCandidateKeysRule()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("Multiple candidate keys do not mean all should become primary keys", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_RulesReferToPrimaryKeyRecommendationSection()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("<primary_key_recommendation>", prompt);
        Assert.Contains("RECOMMENDED_PRIMARY_KEY", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_RulesContainBusinessIdentifierUniqueGuidance()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("UNIQUE constraint", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_RulesContainValueColumnPrimaryKeyWarning()
    {
        var prompt = ChatContextBuilder.BuildSystemPrompt(new ChatContext());

        Assert.Contains("measurement", prompt, StringComparison.OrdinalIgnoreCase);
    }
}

// ── Normalization full column details ─────────────────────────────────────────

public class ChatContextBuilderNormalizationColumnTests
{
    [Fact]
    public void BuildSystemPrompt_WithProposal_EmitsNormalizationAvailable()
    {
        var ctx = new ChatContext
        {
            NormalizationProposal = new NormalizationProposal
            {
                Tables = [new ProposedTable { TableName = "employees", Columns = [] }],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("NORMALIZATION_AVAILABLE", prompt);
    }

    [Fact]
    public void BuildNormalizationSection_WithProposal_DoesNotContainNotRun()
    {
        var section = ChatContextBuilder.BuildNormalizationSection(new NormalizationProposal
        {
            Tables = [new ProposedTable { TableName = "employees", Columns = [] }],
        });

        Assert.DoesNotContain("NORMALIZATION_NOT_RUN", section);
        Assert.Contains("NORMALIZATION_AVAILABLE", section);
    }

    [Fact]
    public void BuildSystemPrompt_WithProposal_IncludesAllColumnDetails()
    {
        var ctx = new ChatContext
        {
            NormalizationProposal = new NormalizationProposal
            {
                Tables =
                [
                    new ProposedTable
                    {
                        TableName = "orders",
                        Columns   =
                        [
                            new ProposedColumn
                            {
                                Name         = "id",
                                PostgresType = "integer",
                                IsNullable   = false,
                                IsPrimaryKey = true,
                                ForeignKeyTo = null,
                            },
                            new ProposedColumn
                            {
                                Name         = "customer_id",
                                PostgresType = "integer",
                                IsNullable   = false,
                                IsPrimaryKey = false,
                                ForeignKeyTo = "customers(id)",
                            },
                            new ProposedColumn
                            {
                                Name         = "notes",
                                PostgresType = "text",
                                IsNullable   = true,
                                IsPrimaryKey = false,
                                ForeignKeyTo = null,
                            },
                        ],
                    },
                ],
            },
        };

        var prompt = ChatContextBuilder.BuildSystemPrompt(ctx);

        Assert.Contains("id", prompt);
        Assert.Contains("integer", prompt);
        Assert.Contains("PRIMARY KEY", prompt);
        Assert.Contains("customer_id", prompt);
        Assert.Contains("customers(id)", prompt);
        Assert.Contains("FK →", prompt);
        Assert.Contains("notes", prompt);
        Assert.Contains("text", prompt);
        Assert.Contains("NULL", prompt);
    }

    [Fact]
    public void BuildNormalizationSection_NullProposal_ReturnsNotRun()
    {
        var section = ChatContextBuilder.BuildNormalizationSection(null);

        Assert.Contains("NORMALIZATION_NOT_RUN", section);
        Assert.Contains("<normalization_status>", section);
        Assert.Contains("</normalization_status>", section);
    }

    [Fact]
    public void BuildNormalizationSection_EmptyTables_ReturnsNotRun()
    {
        var section = ChatContextBuilder.BuildNormalizationSection(
            new NormalizationProposal { Tables = [] });

        Assert.Contains("NORMALIZATION_NOT_RUN", section);
    }

    [Fact]
    public void BuildSchemaSection_NullSchema_ReturnsNotInferred()
    {
        var section = ChatContextBuilder.BuildSchemaSection(null);

        Assert.Contains("SCHEMA_NOT_INFERRED", section);
        Assert.Contains("<inferred_schema>", section);
        Assert.Contains("</inferred_schema>", section);
    }

    [Fact]
    public void BuildValidationSection_NullValidation_ReturnsNotRun()
    {
        var section = ChatContextBuilder.BuildValidationSection(null);

        Assert.Contains("VALIDATION_NOT_RUN", section);
        Assert.Contains("<validation_results>", section);
        Assert.Contains("</validation_results>", section);
    }

    [Fact]
    public void BuildAnalysisSection_NullAnalysis_ReturnsNotRun()
    {
        var section = ChatContextBuilder.BuildAnalysisSection(null);

        Assert.Contains("ANALYSIS_NOT_RUN", section);
        Assert.Contains("<data_analysis>", section);
        Assert.Contains("</data_analysis>", section);
    }
}
