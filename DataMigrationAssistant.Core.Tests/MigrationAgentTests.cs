using DataMigrationAssistant.Core.Agents;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class AgentFixtures
{
    public static TableSchema SimpleSchema(params (string name, PostgresType type, bool nullable, bool candidateKey)[] cols) =>
        new()
        {
            TableName      = "test_table",
            SheetName      = "Sheet1",
            SampleRowCount = 10,
            Columns        = cols.Select((c, i) => new ColumnSchema
            {
                Index          = i,
                Name           = c.name,
                SnakeCaseName  = c.name,
                InferredType   = c.type,
                IsNullable     = c.nullable,
                IsCandidateKey = c.candidateKey,
            }).ToList(),
        };

    public static ValidationResult CleanValidation() =>
        new() { CanProceed = true, Warnings = [] };

    public static ValidationResult WithWarnings(params ValidationWarning[] warnings) =>
        new() { CanProceed = true, Warnings = warnings };

    public static MigrationAgentContext MakeContext(string question, ChatContext? chatContext = null) =>
        new()
        {
            Question    = question,
            ChatContext = chatContext ?? new ChatContext(),
        };

    public static DataAnalysisResult SimpleAnalysisResult(
        string summary = "Dataset looks good.",
        IReadOnlyList<DataAnalysisFinding>?       findings        = null,
        IReadOnlyList<DataAnalysisFinding>?       risks           = null,
        IReadOnlyList<DataAnalysisRecommendation>? recommendations = null) =>
        new()
        {
            Summary         = summary,
            Findings        = findings        ?? [],
            Risks           = risks           ?? [],
            Recommendations = recommendations ?? [],
        };

    public static NormalizationProposal SimpleProposal(string reasoning = "Reduce redundancy.") =>
        new()
        {
            Reasoning = reasoning,
            Tables =
            [
                new ProposedTable
                {
                    TableName = "customers",
                    Columns   =
                    [
                        new ProposedColumn { Name = "id",   PostgresType = "integer", IsPrimaryKey = true },
                        new ProposedColumn { Name = "name", PostgresType = "text",    IsNullable   = false },
                    ],
                },
            ],
        };
}

// ── MigrationAgentRouter ──────────────────────────────────────────────────────

public class MigrationAgentRouterTests
{
    private static MigrationAgentRouter MakeRouter() =>
        new(
            new GtnAgent(),
            new SchemaAgent(),
            new ValidationAgent(),
            new DataAnalysisAgent(),
            new NormalizationAgent(),
            new SqlGenerationAgent(),
            new GeneralMigrationAgent(MakeNullFactory()));

    private static IChatAssistantServiceFactory MakeNullFactory() =>
        new ChatAssistantServiceFactory(
            new ClaudeChatAssistantService(null),
            new OllamaChatAssistantService(new HttpClient()),
            new NullChatAssistantService());

    [Theory]
    [InlineData("What schema does this table have?")]
    [InlineData("List all columns in the table")]
    [InlineData("What types are inferred?")]
    [InlineData("Which columns are nullable?")]
    [InlineData("What is the candidate key?")]
    [InlineData("What is the primary key?")]
    [InlineData("What candidate keys exist?")]
    public void Route_SchemaQuestion_ReturnsSchemaAgent(string question)
    {
        var agent = MakeRouter().Route(question);
        Assert.IsType<SchemaAgent>(agent);
    }

    [Theory]
    [InlineData("Are there any validation warnings?")]
    [InlineData("Can I proceed with migration?")]
    [InlineData("Show me the validation results")]
    [InlineData("Are there duplicate risks?")]
    [InlineData("Are there nullability issues?")]
    public void Route_ValidationQuestion_ReturnsValidationAgent(string question)
    {
        var agent = MakeRouter().Route(question);
        Assert.IsType<ValidationAgent>(agent);
    }

    [Theory]
    [InlineData("What does the data analysis show?")]
    [InlineData("What risks do you see?")]
    [InlineData("What are your recommendations?")]
    [InlineData("What are the findings?")]
    [InlineData("What is the data quality like?")]
    [InlineData("What are the unique constraints?")]
    [InlineData("What is the primary key recommendation?")]
    [InlineData("Should username be the primary key?")]
    [InlineData("Which primary key should I use?")]
    [InlineData("What is the best key?")]
    [InlineData("What is the recommended primary key?")]
    [InlineData("What unique constraints should I add?")]
    public void Route_DataAnalysisQuestion_ReturnsDataAnalysisAgent(string question)
    {
        var agent = MakeRouter().Route(question);
        Assert.IsType<DataAnalysisAgent>(agent);
    }

    [Theory]
    [InlineData("Explain the normalization proposal")]
    [InlineData("How should I normalize this table?")]
    [InlineData("What are the proposed tables?")]
    [InlineData("What are the foreign keys?")]
    [InlineData("What are the table relationships?")]
    [InlineData("Should I split this table?")]
    public void Route_NormalizationQuestion_ReturnsNormalizationAgent(string question)
    {
        var agent = MakeRouter().Route(question);
        Assert.IsType<NormalizationAgent>(agent);
    }

    [Theory]
    [InlineData("What SQL was generated?")]
    [InlineData("Show me the create table statement")]
    [InlineData("What does the seed SQL look like?")]
    [InlineData("How do I download the seed file?")]
    [InlineData("What is the diff?")]
    [InlineData("What are the differences between the two files?")]
    [InlineData("What generated files are available?")]
    [InlineData("How do I download migration SQL?")]
    public void Route_SqlQuestion_ReturnsSqlGenerationAgent(string question)
    {
        var agent = MakeRouter().Route(question);
        Assert.IsType<SqlGenerationAgent>(agent);
    }

    [Theory]
    [InlineData("What GTN scenarios are there?")]
    [InlineData("How many scenarios were generated?")]
    [InlineData("Show me the GTN seed")]
    [InlineData("What are the validation groups?")]
    [InlineData("What are the pay elements?")]
    [InlineData("What payroll data was loaded?")]
    public void Route_GtnQuestion_ReturnsGtnAgent(string question)
    {
        var agent = MakeRouter().Route(question);
        Assert.IsType<GtnAgent>(agent);
    }

    [Theory]
    [InlineData("How do I migrate this dataset?")]
    [InlineData("")]
    [InlineData("random question")]
    public void Route_UnknownQuestion_ReturnsGeneralMigrationAgent(string question)
    {
        var agent = MakeRouter().Route(question);
        Assert.IsType<GeneralMigrationAgent>(agent);
    }

    [Fact]
    public void Route_NullabilityQuestion_GoesToValidationNotSchema()
    {
        // "nullability" contains "nullable" but ValidationAgent should win because
        // SchemaAgent.CanHandle excludes "nullability" via word-boundary check
        var agent = MakeRouter().Route("Are there nullability issues?");
        Assert.IsType<ValidationAgent>(agent);
    }

    [Fact]
    public void Route_NullableQuestion_GoesToSchema()
    {
        var agent = MakeRouter().Route("Which columns are nullable?");
        Assert.IsType<SchemaAgent>(agent);
    }

    [Fact]
    public void Route_PrimaryKeyRecommendation_GoesToDataAnalysisNotSchema()
    {
        // "primary key recommendation" belongs to DataAnalysisAgent
        var agent = MakeRouter().Route("What is the primary key recommendation?");
        Assert.IsType<DataAnalysisAgent>(agent);
    }

    [Fact]
    public void Route_ShouldUsernameBeThePrimaryKey_GoesToDataAnalysis()
    {
        var agent = MakeRouter().Route("Should username be the primary key?");
        Assert.IsType<DataAnalysisAgent>(agent);
    }

    [Fact]
    public void Route_WhichPrimaryKeyShouldIUse_GoesToDataAnalysis()
    {
        var agent = MakeRouter().Route("Which primary key should I use?");
        Assert.IsType<DataAnalysisAgent>(agent);
    }

    [Fact]
    public void Route_WhatIsThePrimaryKey_GoesToSchema()
    {
        var agent = MakeRouter().Route("What is the primary key?");
        Assert.IsType<SchemaAgent>(agent);
    }

    [Fact]
    public void Route_WhatCandidateKeysExist_GoesToSchema()
    {
        var agent = MakeRouter().Route("What candidate keys exist?");
        Assert.IsType<SchemaAgent>(agent);
    }

    [Fact]
    public void Route_RiskQuestion_GoesToDataAnalysisNotValidation()
    {
        // "risk" alone (without "duplicate"/"warning" etc.) routes to DataAnalysisAgent
        var agent = MakeRouter().Route("What risks do you see?");
        Assert.IsType<DataAnalysisAgent>(agent);
    }
}

// ── SchemaAgent ───────────────────────────────────────────────────────────────

public class SchemaAgentTests
{
    private readonly SchemaAgent _sut = new();

    [Fact]
    public async Task HandleAsync_NoSchema_ReturnsNotInferredMessage()
    {
        var ctx = AgentFixtures.MakeContext("What schema do I have?");
        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("Schema Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("Schema Inference", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_WithSchema_ReturnsSchemaAgent()
    {
        var schema = AgentFixtures.SimpleSchema(
            ("id",   PostgresType.Integer, false, true),
            ("name", PostgresType.Text,    true,  false));

        var ctx = AgentFixtures.MakeContext(
            "What schema does this table have?",
            new ChatContext { Schema = schema });

        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("Schema Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("test_table", response.Answer);
        Assert.Contains("id", response.Answer);
        Assert.Contains("name", response.Answer);
        Assert.Single(response.Sources);
    }

    [Fact]
    public async Task HandleAsync_TypeQuestion_ListsColumnTypes()
    {
        var schema = AgentFixtures.SimpleSchema(
            ("id",    PostgresType.Integer, false, true),
            ("email", PostgresType.Text,    false, false));

        var ctx = AgentFixtures.MakeContext(
            "What types are inferred for each column?",
            new ChatContext { Schema = schema });

        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("Inferred column types", response.Answer);
        Assert.Contains("Integer", response.Answer);
        Assert.Contains("Text", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_NullableQuestion_ListsNullableColumns()
    {
        var schema = AgentFixtures.SimpleSchema(
            ("id",    PostgresType.Integer, false, true),
            ("notes", PostgresType.Text,    true,  false));

        var ctx = AgentFixtures.MakeContext(
            "Which columns are nullable?",
            new ChatContext { Schema = schema });

        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("NOT NULL", response.Answer);
        Assert.Contains("Nullable", response.Answer);
        Assert.Contains("notes", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_CandidateKeyQuestion_ListsCandidateKeys()
    {
        var schema = AgentFixtures.SimpleSchema(
            ("id",   PostgresType.Integer, false, true),
            ("name", PostgresType.Text,    false, false));

        var ctx = AgentFixtures.MakeContext(
            "What is the candidate key?",
            new ChatContext { Schema = schema });

        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("Candidate key", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_PrimaryKeyQuestion_ReturnsRecommendation()
    {
        var schema = AgentFixtures.SimpleSchema(
            ("id",   PostgresType.Integer, false, true),
            ("code", PostgresType.Text,    false, true));

        var ctx = AgentFixtures.MakeContext(
            "Which primary key should I use?",
            new ChatContext { Schema = schema });

        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("Recommended primary key", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task HandleAsync_NoCandidateKeys_SuggestsSurrogateKey()
    {
        var schema = AgentFixtures.SimpleSchema(
            ("name",  PostgresType.Text,    false, false),
            ("value", PostgresType.Integer, false, false));

        var ctx = AgentFixtures.MakeContext(
            "Which primary key should I use?",
            new ChatContext { Schema = schema });

        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("surrogate key", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanHandle_SchemaKeywords_ReturnsTrue()
    {
        Assert.True(_sut.CanHandle("What is the schema?"));
        Assert.True(_sut.CanHandle("List columns"));
        Assert.True(_sut.CanHandle("What type is email?"));
        Assert.True(_sut.CanHandle("Which columns are nullable?"));
        Assert.True(_sut.CanHandle("Is id a candidate key?"));
        Assert.True(_sut.CanHandle("What is the primary key?"));
        Assert.True(_sut.CanHandle("What candidate keys exist?"));
    }

    [Fact]
    public void CanHandle_RecommendationKeyQuestions_ReturnsFalse()
    {
        // Recommendation intent + key term → DataAnalysisAgent owns these
        Assert.False(_sut.CanHandle("What is the primary key recommendation?"));
        Assert.False(_sut.CanHandle("Should username be the primary key?"));
        Assert.False(_sut.CanHandle("Which primary key should I use?"));
        Assert.False(_sut.CanHandle("What is the best key?"));
        Assert.False(_sut.CanHandle("What is the recommended primary key?"));
        Assert.False(_sut.CanHandle("What unique constraints should I add?"));
    }

    [Fact]
    public void CanHandle_NullabilityKeyword_ReturnsFalse()
    {
        // "nullability" belongs to ValidationAgent
        Assert.False(_sut.CanHandle("Are there nullability issues?"));
    }

    [Fact]
    public void CanHandle_UnrelatedQuestion_ReturnsFalse()
    {
        Assert.False(_sut.CanHandle("How do I migrate this dataset?"));
    }
}

// ── ValidationAgent ───────────────────────────────────────────────────────────

public class ValidationAgentTests
{
    private readonly ValidationAgent _sut = new();

    [Fact]
    public async Task HandleAsync_NoValidation_ReturnsNotRunMessage()
    {
        var ctx = AgentFixtures.MakeContext("Are there warnings?");
        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("Validation Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("Validation tab", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_CleanValidation_ReturnsCleanMessage()
    {
        var ctx = AgentFixtures.MakeContext(
            "Are there any validation warnings?",
            new ChatContext { Validation = AgentFixtures.CleanValidation() });

        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("Validation Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("No warnings", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_ProceedQuestion_ReturnsStatus()
    {
        var ctx = AgentFixtures.MakeContext(
            "Can I proceed with the migration?",
            new ChatContext { Validation = AgentFixtures.CleanValidation() });

        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("Can proceed", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Yes", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_DuplicateQuestion_ReturnsDuplicateWarnings()
    {
        var validation = AgentFixtures.WithWarnings(
            new ValidationWarning
            {
                Code       = "DUPLICATE_VALUES",
                Severity   = ValidationSeverity.Warning,
                Message    = "Duplicate values found",
                ColumnName = "email",
            });

        var ctx = AgentFixtures.MakeContext(
            "Are there duplicate risks?",
            new ChatContext { Validation = validation });

        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("Duplicate", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DUPLICATE_VALUES", response.Answer);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task HandleAsync_NullabilityQuestion_ReturnsNullWarnings()
    {
        var validation = AgentFixtures.WithWarnings(
            new ValidationWarning
            {
                Code       = "NULL_VALUES_IN_NON_NULL_COLUMN",
                Severity   = ValidationSeverity.Warning,
                Message    = "Null values in non-null column",
                ColumnName = "id",
            });

        var ctx = AgentFixtures.MakeContext(
            "Are there nullability issues?",
            new ChatContext { Validation = validation });

        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("Nullability", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NULL_VALUES_IN_NON_NULL_COLUMN", response.Answer);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task HandleAsync_DuplicateQuestion_NoDuplicateWarnings_ReturnsClearMessage()
    {
        var ctx = AgentFixtures.MakeContext(
            "Are there duplicate risks?",
            new ChatContext { Validation = AgentFixtures.CleanValidation() });

        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("No duplicate", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanHandle_ValidationKeywords_ReturnsTrue()
    {
        Assert.True(_sut.CanHandle("Are there validation warnings?"));
        Assert.True(_sut.CanHandle("Show warnings"));
        Assert.True(_sut.CanHandle("Can I proceed with migration?"));
        Assert.True(_sut.CanHandle("Duplicate risks?"));
        Assert.True(_sut.CanHandle("Are there nullability issues?"));
    }

    [Fact]
    public void CanHandle_UnrelatedQuestion_ReturnsFalse()
    {
        Assert.False(_sut.CanHandle("How do I run migration?"));
        Assert.False(_sut.CanHandle("What schema does this table have?"));
    }
}

// ── DataAnalysisAgent ─────────────────────────────────────────────────────────

public class DataAnalysisAgentTests
{
    private readonly DataAnalysisAgent _sut = new();

    [Fact]
    public async Task HandleAsync_NoAnalysisResult_ReturnsMissingMessage()
    {
        var ctx      = AgentFixtures.MakeContext("What are the risks?");
        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("Data Analysis Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("Data Analysis", response.Answer);
        Assert.Contains("run", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_WithAnalysisResult_ReturnsSummary()
    {
        var result = AgentFixtures.SimpleAnalysisResult(
            summary  : "Dataset has 100 rows with good quality.",
            findings : [new DataAnalysisFinding { Category = "Schema", Severity = "Info", Description = "3 columns" }]);

        var ctx      = AgentFixtures.MakeContext("What does the analysis show?", new ChatContext { AnalysisResult = result });
        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("Data Analysis Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("100 rows", response.Answer);
        Assert.Single(response.Sources);
    }

    [Fact]
    public async Task HandleAsync_RiskQuestion_ReturnsRisks()
    {
        var result = AgentFixtures.SimpleAnalysisResult(
            risks: [new DataAnalysisFinding { Category = "Integrity", Severity = "High", Description = "Duplicate primary keys" }]);

        var ctx      = AgentFixtures.MakeContext("What risks do you see?", new ChatContext { AnalysisResult = result });
        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("risk", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Duplicate primary keys", response.Answer);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task HandleAsync_RiskQuestion_NoRisks_ReturnsNoRisksMessage()
    {
        var result   = AgentFixtures.SimpleAnalysisResult();
        var ctx      = AgentFixtures.MakeContext("What risks do you see?", new ChatContext { AnalysisResult = result });
        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("No risks", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_RecommendationQuestion_ReturnsRecommendations()
    {
        var result = AgentFixtures.SimpleAnalysisResult(
            recommendations: [new DataAnalysisRecommendation { Priority = "High", Type = "Index", Description = "Add index on email" }]);

        var ctx      = AgentFixtures.MakeContext("What are your recommendations?", new ChatContext { AnalysisResult = result });
        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("Recommendation", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Add index on email", response.Answer);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task HandleAsync_FindingQuestion_ReturnsFindings()
    {
        var result = AgentFixtures.SimpleAnalysisResult(
            findings: [new DataAnalysisFinding { Category = "Cardinality", Severity = "Info", Description = "High cardinality column detected", Detail = "email column" }]);

        var ctx      = AgentFixtures.MakeContext("What are the findings?", new ChatContext { AnalysisResult = result });
        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("Finding", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("High cardinality", response.Answer);
        Assert.Contains("email column", response.Answer);
    }

    [Fact]
    public void CanHandle_DataAnalysisKeywords_ReturnsTrue()
    {
        Assert.True(_sut.CanHandle("What does the analysis show?"));
        Assert.True(_sut.CanHandle("What are the risks?"));
        Assert.True(_sut.CanHandle("What are the recommendations?"));
        Assert.True(_sut.CanHandle("What are the findings?"));
        Assert.True(_sut.CanHandle("What is the data quality?"));
        Assert.True(_sut.CanHandle("What are the unique constraints?"));
        Assert.True(_sut.CanHandle("What is the primary key recommendation?"));
    }

    [Fact]
    public void CanHandle_RecommendationIntentPlusKeyTerm_ReturnsTrue()
    {
        Assert.True(_sut.CanHandle("Should username be the primary key?"));
        Assert.True(_sut.CanHandle("Which primary key should I use?"));
        Assert.True(_sut.CanHandle("What is the best key?"));
        Assert.True(_sut.CanHandle("What is the recommended primary key?"));
        Assert.True(_sut.CanHandle("What unique constraints should I add?"));
    }

    [Fact]
    public void CanHandle_UnrelatedQuestion_ReturnsFalse()
    {
        Assert.False(_sut.CanHandle("How do I migrate?"));
        Assert.False(_sut.CanHandle("What schema does this table have?"));
        Assert.False(_sut.CanHandle("Can I proceed?"));
    }
}

// ── NormalizationAgent ────────────────────────────────────────────────────────

public class NormalizationAgentTests
{
    private readonly NormalizationAgent _sut = new();

    [Fact]
    public async Task HandleAsync_NoProposal_ReturnsMissingMessage()
    {
        var ctx      = AgentFixtures.MakeContext("What are the proposed tables?");
        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("Normalization Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("normalization proposal", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_EmptyProposal_ReturnsMissingMessage()
    {
        var proposal = new NormalizationProposal { Tables = [] };
        var ctx      = AgentFixtures.MakeContext("What are the proposed tables?", new ChatContext { NormalizationProposal = proposal });
        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("Normalization Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("normalization proposal", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_WithProposal_ReturnsTableDetails()
    {
        var proposal = AgentFixtures.SimpleProposal("Table has repeating groups.");
        var ctx      = AgentFixtures.MakeContext("Explain the normalization proposal", new ChatContext { NormalizationProposal = proposal });
        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("Normalization Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("customers", response.Answer);
        Assert.Contains("Table has repeating groups", response.Answer);
        Assert.Single(response.Sources);
    }

    [Fact]
    public async Task HandleAsync_ForeignKeyQuestion_ReturnsForeignKeys()
    {
        var proposal = new NormalizationProposal
        {
            Tables =
            [
                new ProposedTable
                {
                    TableName = "orders",
                    Columns   =
                    [
                        new ProposedColumn { Name = "id",          PostgresType = "integer", IsPrimaryKey = true },
                        new ProposedColumn { Name = "customer_id", PostgresType = "integer", ForeignKeyTo = "customers(id)" },
                    ],
                },
            ],
        };

        var ctx      = AgentFixtures.MakeContext("What are the foreign keys?", new ChatContext { NormalizationProposal = proposal });
        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("customer_id", response.Answer);
        Assert.Contains("customers(id)", response.Answer);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task HandleAsync_ForeignKeyQuestion_NoForeignKeys_ReturnsClearMessage()
    {
        var proposal = AgentFixtures.SimpleProposal();
        var ctx      = AgentFixtures.MakeContext("What are the foreign keys?", new ChatContext { NormalizationProposal = proposal });
        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("No foreign key", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_ProposedTableQuestion_ListsTableNames()
    {
        var proposal = AgentFixtures.SimpleProposal();
        var ctx      = AgentFixtures.MakeContext("What are the proposed tables?", new ChatContext { NormalizationProposal = proposal });
        var response = await _sut.HandleAsync(ctx);

        Assert.Contains("Proposed tables", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("customers", response.Answer);
    }

    [Fact]
    public void CanHandle_NormalizationKeywords_ReturnsTrue()
    {
        Assert.True(_sut.CanHandle("Explain the normalization proposal"));
        Assert.True(_sut.CanHandle("How should I normalize this table?"));
        Assert.True(_sut.CanHandle("What are the proposed tables?"));
        Assert.True(_sut.CanHandle("What are the foreign keys?"));
        Assert.True(_sut.CanHandle("What are the table relationships?"));
        Assert.True(_sut.CanHandle("Should I split this table?"));
    }

    [Fact]
    public void CanHandle_UnrelatedQuestion_ReturnsFalse()
    {
        Assert.False(_sut.CanHandle("How do I migrate?"));
        Assert.False(_sut.CanHandle("What schema does this table have?"));
        Assert.False(_sut.CanHandle("What are the validation warnings?"));
    }
}

// ── GeneralMigrationAgent ─────────────────────────────────────────────────────

public class GeneralMigrationAgentTests
{
    private static GeneralMigrationAgent MakeAgent(IChatAssistantService? service = null) =>
        new(new ChatAssistantServiceFactory(
            new ClaudeChatAssistantService(null),
            new OllamaChatAssistantService(new HttpClient()),
            service is NullChatAssistantService nullSvc
                ? nullSvc
                : new NullChatAssistantService()));

    [Fact]
    public void CanHandle_AnyQuestion_AlwaysReturnsTrue()
    {
        var agent = MakeAgent();
        Assert.True(agent.CanHandle("anything"));
        Assert.True(agent.CanHandle(string.Empty));
        Assert.True(agent.CanHandle("schema question normally handled elsewhere"));
    }

    [Fact]
    public async Task HandleAsync_UsesNullProvider_ReturnsAnswer()
    {
        var agent    = MakeAgent(new NullChatAssistantService());
        var ctx      = AgentFixtures.MakeContext("How do I migrate?");
        var response = await agent.HandleAsync(ctx);

        Assert.Equal("General Migration Agent", response.AgentName);
        Assert.False(response.WasHandledLocally);
        Assert.False(string.IsNullOrWhiteSpace(response.Answer));
    }

    [Fact]
    public async Task HandleAsync_PassesHistoryAndContext_ToUnderlyingService()
    {
        var agent   = MakeAgent(new NullChatAssistantService());
        var history = new List<ChatMessage>
        {
            new() { Role = ChatRole.User,      Content = "Hello" },
            new() { Role = ChatRole.Assistant, Content = "Hi there" },
        };

        var ctx = new MigrationAgentContext
        {
            Question    = "How do I migrate?",
            History     = history,
            ChatContext = new ChatContext(),
            Provider    = null,
        };

        var response = await agent.HandleAsync(ctx);

        Assert.NotNull(response.Answer);
    }
}

// ── SqlGenerationAgent ────────────────────────────────────────────────────────

public class SqlGenerationAgentTests
{
    private readonly SqlGenerationAgent _sut = new();

    [Fact]
    public void CanHandle_SqlKeywords_ReturnsTrue()
    {
        Assert.True(_sut.CanHandle("What SQL was generated?"));
        Assert.True(_sut.CanHandle("Show me the create table statement"));
        Assert.True(_sut.CanHandle("What does the seed SQL look like?"));
        Assert.True(_sut.CanHandle("How do I download?"));
        Assert.True(_sut.CanHandle("What is the diff?"));
        Assert.True(_sut.CanHandle("What are the differences between schemas?"));
        Assert.True(_sut.CanHandle("What generated files are available?"));
        Assert.True(_sut.CanHandle("Show me the upsert SQL"));
        Assert.True(_sut.CanHandle("Show me the migration SQL"));
    }

    [Fact]
    public void CanHandle_UnrelatedQuestion_ReturnsFalse()
    {
        Assert.False(_sut.CanHandle("How do I migrate this dataset?"));
        Assert.False(_sut.CanHandle("What schema does this table have?"));
        Assert.False(_sut.CanHandle("random question"));
    }

    [Fact]
    public async Task HandleAsync_NoGeneratedSql_NoSchema_ReturnsLoadFileMessage()
    {
        var ctx      = AgentFixtures.MakeContext("What SQL was generated?");
        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("SQL Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("No SQL has been generated", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Downloads", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_NoGeneratedSql_WithSchema_ExplainsDownloadsTab()
    {
        var schema = AgentFixtures.SimpleSchema(("id", PostgresType.Integer, false, true));
        var ctx    = AgentFixtures.MakeContext(
            "What SQL was generated?",
            new ChatContext { Schema = schema });

        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("SQL Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("No SQL has been generated", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test_table", response.Answer);
        Assert.Contains("Downloads", response.Answer);
        Assert.Contains("seed.sql", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_WithGeneratedSeedSql_SummarizesSeedSql()
    {
        const string fakeSeedSql = "INSERT INTO test_table VALUES (1);\nINSERT INTO test_table VALUES (2);";
        var schema = AgentFixtures.SimpleSchema(("id", PostgresType.Integer, false, true));

        var ctx = AgentFixtures.MakeContext(
            "What SQL was generated?",
            new ChatContext { Schema = schema, GeneratedSeedSql = fakeSeedSql });

        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("SQL Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("seed.sql", response.Answer);
        Assert.Contains("INSERT", response.Answer);
        Assert.Contains("Downloads", response.Answer);
        Assert.Contains("seed.sql", response.Sources);
    }

    [Fact]
    public async Task HandleAsync_WithGeneratedMigrationSql_SummarizesMigrationSql()
    {
        const string fakeMigrationSql = "CREATE TABLE customers (id integer NOT NULL);";
        var schema = AgentFixtures.SimpleSchema(("id", PostgresType.Integer, false, true));

        var ctx = AgentFixtures.MakeContext(
            "What is the migration SQL?",
            new ChatContext { Schema = schema, GeneratedMigrationSql = fakeMigrationSql });

        var response = await _sut.HandleAsync(ctx);

        Assert.True(response.WasHandledLocally);
        Assert.Contains("normalized-schema.sql", response.Answer);
        Assert.Contains("Downloads", response.Answer);
        Assert.Contains("normalized-schema.sql", response.Sources);
    }

    [Fact]
    public async Task HandleAsync_WithGtnResult_SummarizesGtnSql()
    {
        var gtnResult = new GtnSeedGenerationResult
        {
            ScenarioCount = 5,
            ScenariosSql  = "INSERT INTO nomenclature.gtn_scenarios ...",
            Warnings      = [],
        };

        var ctx = AgentFixtures.MakeContext(
            "What SQL files are available?",
            new ChatContext { GtnResult = gtnResult });

        var response = await _sut.HandleAsync(ctx);

        Assert.True(response.WasHandledLocally);
        Assert.Contains("gtn-scenarios-seed.sql", response.Answer);
        Assert.Contains("5", response.Answer);
        Assert.Contains("gtn-scenarios-seed.sql", response.Sources);
    }

    [Fact]
    public async Task HandleAsync_AllSqlGenerated_SummarizesAllOutputs()
    {
        var schema   = AgentFixtures.SimpleSchema(("id", PostgresType.Integer, false, true));
        var gtnResult = new GtnSeedGenerationResult { ScenarioCount = 3, Warnings = [] };

        var ctx = AgentFixtures.MakeContext(
            "What generated files are available?",
            new ChatContext
            {
                Schema                = schema,
                GeneratedSeedSql      = "INSERT INTO test_table VALUES (1);",
                GeneratedMigrationSql = "CREATE TABLE test_table (id integer);",
                GtnResult             = gtnResult,
            });

        var response = await _sut.HandleAsync(ctx);

        Assert.True(response.WasHandledLocally);
        Assert.Contains("seed.sql", response.Answer);
        Assert.Contains("normalized-schema.sql", response.Answer);
        Assert.Contains("gtn-scenarios-seed.sql", response.Answer);
        Assert.Equal(3, response.Sources.Count);
    }
}

// ── GtnAgent ──────────────────────────────────────────────────────────────────

public class GtnAgentTests
{
    private readonly GtnAgent _sut = new();

    [Fact]
    public void CanHandle_GtnKeywords_ReturnsTrue()
    {
        Assert.True(_sut.CanHandle("What GTN scenarios are there?"));
        Assert.True(_sut.CanHandle("Show me the GTN seed"));
        Assert.True(_sut.CanHandle("How many scenarios were generated?"));
        Assert.True(_sut.CanHandle("What are the validation groups?"));
        Assert.True(_sut.CanHandle("What are the pay elements?"));
        Assert.True(_sut.CanHandle("What payroll data was loaded?"));
    }

    [Fact]
    public void CanHandle_UnrelatedQuestion_ReturnsFalse()
    {
        Assert.False(_sut.CanHandle("What schema does this table have?"));
        Assert.False(_sut.CanHandle("How do I migrate?"));
        Assert.False(_sut.CanHandle("What SQL was generated?"));
    }

    [Fact]
    public async Task HandleAsync_NoGtnResult_NoSchema_ExplainsHowToGenerate()
    {
        var ctx      = AgentFixtures.MakeContext("What GTN scenarios are there?");
        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("GTN Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("No GTN seed", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Downloads", response.Answer);
        Assert.Contains("Generate GTN Scenarios", response.Answer);
        Assert.Empty(response.Sources);
    }

    [Fact]
    public async Task HandleAsync_NoGtnResult_WithSchema_IncludesTableName()
    {
        var schema = AgentFixtures.SimpleSchema(("id", PostgresType.Integer, false, true));
        var ctx    = AgentFixtures.MakeContext(
            "What GTN scenarios are there?",
            new ChatContext { Schema = schema });

        var response = await _sut.HandleAsync(ctx);

        Assert.True(response.WasHandledLocally);
        Assert.Contains("test_table", response.Answer);
        Assert.Contains("Downloads", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_GtnResultWithNoWarnings_SummarizesScenarioCount()
    {
        var gtnResult = new GtnSeedGenerationResult
        {
            ScenarioCount = 7,
            ScenariosSql  = "INSERT ...",
            Warnings      = [],
        };

        var ctx = AgentFixtures.MakeContext(
            "How many GTN scenarios were generated?",
            new ChatContext { GtnResult = gtnResult });

        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("GTN Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("7", response.Answer);
        Assert.Contains("No warnings", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gtn-scenarios-seed.sql", response.Answer);
        Assert.Single(response.Sources);
    }

    [Fact]
    public async Task HandleAsync_GtnResultWithWarnings_SummarizesWarnings()
    {
        var gtnResult = new GtnSeedGenerationResult
        {
            ScenarioCount = 3,
            ScenariosSql  = "INSERT ...",
            Warnings      =
            [
                new GtnSeedWarning
                {
                    RowNumber  = 2,
                    ScenarioId = "SC001",
                    Column     = "element_rule_1",
                    Value      = null,
                    Message    = "Missing required value",
                },
            ],
        };

        var ctx = AgentFixtures.MakeContext(
            "Are there any GTN warnings?",
            new ChatContext { GtnResult = gtnResult });

        var response = await _sut.HandleAsync(ctx);

        Assert.Equal("GTN Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
        Assert.Contains("3", response.Answer);
        Assert.Contains("1", response.Answer);
        Assert.Contains("Row 2", response.Answer);
        Assert.Contains("element_rule_1", response.Answer);
        Assert.Contains("Missing required value", response.Answer);
        Assert.Single(response.Sources);
    }
}

// ── MigrationAssistant facade ─────────────────────────────────────────────────

public class MigrationAssistantTests
{
    private static IMigrationAssistant MakeAssistant() =>
        new MigrationAssistant(
            new MigrationAgentRouter(
                new GtnAgent(),
                new SchemaAgent(),
                new ValidationAgent(),
                new DataAnalysisAgent(),
                new NormalizationAgent(),
                new SqlGenerationAgent(),
                new GeneralMigrationAgent(
                    new ChatAssistantServiceFactory(
                        new ClaudeChatAssistantService(null),
                        new OllamaChatAssistantService(new HttpClient()),
                        new NullChatAssistantService()))));

    [Fact]
    public async Task AskAsync_SchemaQuestion_RoutesDeterministically()
    {
        var schema = AgentFixtures.SimpleSchema(("id", PostgresType.Integer, false, true));
        var ctx    = AgentFixtures.MakeContext("What schema does this table have?", new ChatContext { Schema = schema });

        var response = await MakeAssistant().AskAsync(ctx);

        Assert.Equal("Schema Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task AskAsync_ValidationQuestion_RoutesDeterministically()
    {
        var ctx = AgentFixtures.MakeContext(
            "Are there any validation warnings?",
            new ChatContext { Validation = AgentFixtures.CleanValidation() });

        var response = await MakeAssistant().AskAsync(ctx);

        Assert.Equal("Validation Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task AskAsync_DataAnalysisQuestion_RoutesDeterministically()
    {
        var ctx = AgentFixtures.MakeContext(
            "What risks do you see?",
            new ChatContext { AnalysisResult = AgentFixtures.SimpleAnalysisResult() });

        var response = await MakeAssistant().AskAsync(ctx);

        Assert.Equal("Data Analysis Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task AskAsync_NormalizationQuestion_RoutesDeterministically()
    {
        var ctx = AgentFixtures.MakeContext(
            "Explain the normalization proposal",
            new ChatContext { NormalizationProposal = AgentFixtures.SimpleProposal() });

        var response = await MakeAssistant().AskAsync(ctx);

        Assert.Equal("Normalization Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task AskAsync_SqlQuestion_RoutesDeterministically()
    {
        var ctx = AgentFixtures.MakeContext("What SQL was generated?");

        var response = await MakeAssistant().AskAsync(ctx);

        Assert.Equal("SQL Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task AskAsync_GtnQuestion_RoutesDeterministically()
    {
        var ctx = AgentFixtures.MakeContext("How many GTN scenarios are there?");

        var response = await MakeAssistant().AskAsync(ctx);

        Assert.Equal("GTN Agent", response.AgentName);
        Assert.True(response.WasHandledLocally);
    }

    [Fact]
    public async Task AskAsync_UnknownQuestion_FallsBackToGeneralAgent()
    {
        var ctx = AgentFixtures.MakeContext("How do I deploy my database?");

        var response = await MakeAssistant().AskAsync(ctx);

        Assert.Equal("General Migration Agent", response.AgentName);
        Assert.False(response.WasHandledLocally);
    }
}
