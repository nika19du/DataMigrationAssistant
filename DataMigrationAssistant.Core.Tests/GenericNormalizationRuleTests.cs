using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Normalization;

namespace DataMigrationAssistant.Core.Tests;

// ─── Helpers ──────────────────────────────────────────────────────────────────

file static class GenericRuleHelpers
{
    public static NormalizationRequest MakeRequest(
        string tableName,
        IReadOnlyList<ColumnSchema> schemaColumns)
    {
        var previewColumns = schemaColumns
            .Select((c, i) => new ColumnInfo { Index = i, Name = c.SnakeCaseName, SnakeCaseName = c.SnakeCaseName })
            .ToList();

        return new NormalizationRequest
        {
            FlatSchema = new TableSchema
            {
                TableName = tableName,
                Columns   = schemaColumns,
            },
            SheetPreview = new SheetPreview
            {
                SheetName = tableName,
                FilePath  = "test.xlsx",
                Columns   = previewColumns,
                Rows      = [],
            },
        };
    }

    public static ColumnSchema Col(
        string name,
        PostgresType type       = PostgresType.Text,
        bool isNullable         = false,
        bool isCandidateKey     = false) =>
        new()
        {
            Name          = name,
            SnakeCaseName = name,
            InferredType  = type,
            IsNullable    = isNullable,
            IsCandidateKey = isCandidateKey,
        };
}

// ─── CanHandle ────────────────────────────────────────────────────────────────

public class GenericNormalizationRuleCanHandleTests
{
    private readonly GenericNormalizationRule _rule = new();

    [Fact]
    public void CanHandle_EmptyRequest_ReturnsTrue()
        => Assert.True(_rule.CanHandle(new NormalizationRequest()));

    [Fact]
    public void CanHandle_UsersColumns_ReturnsTrue()
    {
        var req = GenericRuleHelpers.MakeRequest("users",
        [
            GenericRuleHelpers.Col("id",    PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("email", PostgresType.Text),
        ]);
        Assert.True(_rule.CanHandle(req));
    }

    [Fact]
    public void CanHandle_ValidationScenarioColumns_AlsoReturnsTrue()
    {
        // GenericNormalizationRule is unconditional — always returns true.
        // (ValidationScenarioNormalizationRule is registered first, so it wins in practice.)
        var req = GenericRuleHelpers.MakeRequest("gtn_scenarios",
        [
            GenericRuleHelpers.Col("validation_scenario_id"),
            GenericRuleHelpers.Col("validation_scenario_label"),
            GenericRuleHelpers.Col("validation_scenario_logic"),
            GenericRuleHelpers.Col("system_element_type"),
            GenericRuleHelpers.Col("element_sub_type"),
            GenericRuleHelpers.Col("element_rule_1"),
        ]);
        Assert.True(_rule.CanHandle(req));
    }

    [Fact]
    public void CanHandle_CompletelyUnrelatedColumns_ReturnsTrue()
    {
        var req = GenericRuleHelpers.MakeRequest("anything",
        [
            GenericRuleHelpers.Col("foo"),
            GenericRuleHelpers.Col("bar"),
        ]);
        Assert.True(_rule.CanHandle(req));
    }
}

// ─── Apply: table structure ───────────────────────────────────────────────────

public class GenericNormalizationRuleApplyTableTests
{
    private readonly GenericNormalizationRule _rule = new();

    [Fact]
    public void Apply_ProducesExactlyOneTable()
    {
        var result = _rule.Apply(UsersRequest());
        Assert.Single(result.Tables);
    }

    [Fact]
    public void Apply_TableNameMatchesFlatSchemaTableName()
    {
        var result = _rule.Apply(UsersRequest());
        Assert.Equal("users", result.Tables[0].TableName);
    }

    [Fact]
    public void Apply_ReasoningMatchesSpec()
    {
        var result = _rule.Apply(UsersRequest());
        Assert.Equal(
            "Generic deterministic fallback: no domain-specific rule matched, " +
            "so the sheet is preserved as a single table.",
            result.Reasoning);
    }

    [Fact]
    public void Apply_SqlFieldsAreEmptyBeforeSqlGeneration()
    {
        var proposal = _rule.Apply(UsersRequest());
        Assert.Equal(string.Empty, proposal.CombinedMigrationSql);
        Assert.Equal(string.Empty, proposal.CombinedSeedSql);
        Assert.Equal(string.Empty, proposal.MarkdownReport);
    }

    private static NormalizationRequest UsersRequest() =>
        GenericRuleHelpers.MakeRequest("users",
        [
            GenericRuleHelpers.Col("id",    PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("email", PostgresType.Text),
            GenericRuleHelpers.Col("notes", PostgresType.Text,    isNullable: true),
        ]);
}

// ─── Apply: primary key selection ────────────────────────────────────────────

public class GenericNormalizationRuleApplyPrimaryKeyTests
{
    private readonly GenericNormalizationRule _rule = new();

    [Fact]
    public void Apply_IdCandidateKey_IsMarkedAsPrimaryKey()
    {
        var table = Apply("users",
        [
            GenericRuleHelpers.Col("id",    PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("email", PostgresType.Text),
        ]);

        Assert.Equal("id", table.Columns.Single(c => c.IsPrimaryKey).Name);
    }

    [Fact]
    public void Apply_IdCandidateKey_NoSyntheticColumnPrepended()
    {
        var table = Apply("users",
        [
            GenericRuleHelpers.Col("id",    PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("email", PostgresType.Text),
        ]);

        // Exactly the two source columns — no extra synthetic one
        Assert.Equal(2, table.Columns.Count);
    }

    [Fact]
    public void Apply_OtherCandidateKey_IsMarkedAsPrimaryKey()
    {
        var table = Apply("products",
        [
            GenericRuleHelpers.Col("sku",   PostgresType.Text,    isCandidateKey: true),
            GenericRuleHelpers.Col("price", PostgresType.Numeric),
        ]);

        Assert.Equal("sku", table.Columns.Single(c => c.IsPrimaryKey).Name);
    }

    [Fact]
    public void Apply_IdPreferredOverOtherCandidateKeyEvenWhenListedLast()
    {
        // 'code' appears first in schema but 'id' is the named preference
        var table = Apply("items",
        [
            GenericRuleHelpers.Col("code", PostgresType.Text,    isCandidateKey: true),
            GenericRuleHelpers.Col("id",   PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("name", PostgresType.Text,    isNullable: true),
        ]);

        Assert.Equal("id", table.Columns.Single(c => c.IsPrimaryKey).Name);
    }

    [Fact]
    public void Apply_NoCandidateKey_AddsSyntheticId()
    {
        var table = Apply("logs",
        [
            GenericRuleHelpers.Col("message",   PostgresType.Text),
            GenericRuleHelpers.Col("timestamp", PostgresType.Timestamp),
        ]);

        var pk = table.Columns.Single(c => c.IsPrimaryKey);
        Assert.Equal("id",      pk.Name);
        Assert.Equal("INTEGER", pk.PostgresType);
        Assert.False(pk.IsNullable);
    }

    [Fact]
    public void Apply_NoCandidateKey_SyntheticIdIsFirstColumn()
    {
        var table = Apply("logs",
        [
            GenericRuleHelpers.Col("message", PostgresType.Text),
            GenericRuleHelpers.Col("level",   PostgresType.Text),
        ]);

        Assert.Equal("id", table.Columns[0].Name);
        Assert.True(table.Columns[0].IsPrimaryKey);
    }

    [Fact]
    public void Apply_HasExactlyOnePrimaryKey_NativePk()
    {
        var table = Apply("users",
        [
            GenericRuleHelpers.Col("id",    PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("email", PostgresType.Text),
        ]);

        Assert.Single(table.Columns, c => c.IsPrimaryKey);
    }

    [Fact]
    public void Apply_HasExactlyOnePrimaryKey_SyntheticPk()
    {
        var table = Apply("logs",
        [
            GenericRuleHelpers.Col("message", PostgresType.Text),
            GenericRuleHelpers.Col("level",   PostgresType.Text),
        ]);

        Assert.Single(table.Columns, c => c.IsPrimaryKey);
    }

    private ProposedTable Apply(string tableName, IReadOnlyList<ColumnSchema> columns)
        => _rule.Apply(GenericRuleHelpers.MakeRequest(tableName, columns)).Tables[0];
}

// ─── Apply: column preservation and FK absence ────────────────────────────────

public class GenericNormalizationRuleApplyColumnTests
{
    private readonly GenericNormalizationRule _rule = new();

    [Fact]
    public void Apply_PreservesAllColumnNames()
    {
        var table = Apply("users",
        [
            GenericRuleHelpers.Col("id",    PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("email", PostgresType.Text),
            GenericRuleHelpers.Col("score", PostgresType.Numeric, isNullable: true),
        ]);

        var names = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("id",    names);
        Assert.Contains("email", names);
        Assert.Contains("score", names);
    }

    [Fact]
    public void Apply_PreservesNullability_NonNullable()
    {
        var table = Apply("t",
        [
            GenericRuleHelpers.Col("id",   PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("code", PostgresType.Text,    isNullable: false),
        ]);

        Assert.False(table.Columns.Single(c => c.Name == "code").IsNullable);
    }

    [Fact]
    public void Apply_PreservesNullability_Nullable()
    {
        var table = Apply("t",
        [
            GenericRuleHelpers.Col("id",    PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("notes", PostgresType.Text,    isNullable: true),
        ]);

        Assert.True(table.Columns.Single(c => c.Name == "notes").IsNullable);
    }

    [Theory]
    [InlineData(PostgresType.Boolean,   "BOOLEAN")]
    [InlineData(PostgresType.Integer,   "INTEGER")]
    [InlineData(PostgresType.BigInt,    "BIGINT")]
    [InlineData(PostgresType.Numeric,   "NUMERIC")]
    [InlineData(PostgresType.Date,      "DATE")]
    [InlineData(PostgresType.Timestamp, "TIMESTAMP")]
    [InlineData(PostgresType.Text,      "TEXT")]
    public void Apply_MapsPostgresTypeCorrectly(PostgresType input, string expected)
    {
        var table = Apply("t",
        [
            GenericRuleHelpers.Col("col", input, isCandidateKey: true),
        ]);

        Assert.Equal(expected, table.Columns.Single(c => c.Name == "col").PostgresType);
    }

    [Fact]
    public void Apply_NoForeignKeyOnAnyColumn()
    {
        var table = Apply("orders",
        [
            GenericRuleHelpers.Col("id",          PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("customer_id", PostgresType.Integer),
            GenericRuleHelpers.Col("amount",      PostgresType.Numeric),
        ]);

        Assert.DoesNotContain(table.Columns, c => c.ForeignKeyTo is not null);
    }

    [Fact]
    public void Apply_AllSourceColumnsIncluded()
    {
        var table = Apply("users",
        [
            GenericRuleHelpers.Col("id",    PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("email", PostgresType.Text),
            GenericRuleHelpers.Col("notes", PostgresType.Text,    isNullable: true),
        ]);

        Assert.Contains("id",    table.SourceColumns);
        Assert.Contains("email", table.SourceColumns);
        Assert.Contains("notes", table.SourceColumns);
    }

    [Fact]
    public void Apply_SyntheticId_NotInSourceColumns()
    {
        var table = Apply("logs",
        [
            GenericRuleHelpers.Col("message", PostgresType.Text),
            GenericRuleHelpers.Col("level",   PostgresType.Text),
        ]);

        Assert.Contains("message", table.SourceColumns);
        Assert.Contains("level",   table.SourceColumns);
        Assert.DoesNotContain("id", table.SourceColumns);
    }

    private ProposedTable Apply(string tableName, IReadOnlyList<ColumnSchema> columns)
        => _rule.Apply(GenericRuleHelpers.MakeRequest(tableName, columns)).Tables[0];
}

// ─── Priority: ValidationScenarioRule must win over GenericNormalizationRule ──

public class GenericNormalizationRulePriorityTests
{
    [Fact]
    public void ValidationScenarioRule_WinsOverGenericFallback()
    {
        // Domain-specific rule is first; it must produce 2 tables for GTN schema.
        var sut = new DeterministicNormalizationService(
        [
            new ValidationScenarioNormalizationRule(),
            new GenericNormalizationRule(),
        ]);

        var request = GenericRuleHelpers.MakeRequest("gtn_scenarios",
        [
            GenericRuleHelpers.Col("validation_scenario_id"),
            GenericRuleHelpers.Col("validation_scenario_label"),
            GenericRuleHelpers.Col("validation_scenario_logic"),
            GenericRuleHelpers.Col("system_element_type"),
            GenericRuleHelpers.Col("element_sub_type"),
            GenericRuleHelpers.Col("element_rule_1"),
        ]);

        var result = sut.TryNormalize(request);

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Tables.Count);
        Assert.Contains(result.Value.Tables, t => t.TableName == "gtn_scenarios");
        Assert.Contains(result.Value.Tables, t => t.TableName == "gtn_scenario_settings");
    }

    [Fact]
    public void GenericFallback_UsedWhenNoOtherRuleMatches()
    {
        var sut = new DeterministicNormalizationService(
        [
            new ValidationScenarioNormalizationRule(),
            new GenericNormalizationRule(),
        ]);

        var request = GenericRuleHelpers.MakeRequest("employees",
        [
            GenericRuleHelpers.Col("id",         PostgresType.Integer, isCandidateKey: true),
            GenericRuleHelpers.Col("first_name",  PostgresType.Text),
            GenericRuleHelpers.Col("last_name",   PostgresType.Text),
        ]);

        var result = sut.TryNormalize(request);

        Assert.True(result.Success);
        Assert.Single(result.Value!.Tables);
        Assert.Equal("employees", result.Value.Tables[0].TableName);
    }

    [Fact]
    public void GenericFallback_AnySchemaSucceeds_NoPreviousMatchRequired()
    {
        var sut = new DeterministicNormalizationService(
        [
            new ValidationScenarioNormalizationRule(),
            new GenericNormalizationRule(),
        ]);

        var request = GenericRuleHelpers.MakeRequest("random_sheet",
        [
            GenericRuleHelpers.Col("col_a", PostgresType.Text, isNullable: true),
            GenericRuleHelpers.Col("col_b", PostgresType.Text, isNullable: true),
        ]);

        var result = sut.TryNormalize(request);

        Assert.True(result.Success);
        Assert.Single(result.Value!.Tables);
    }

    [Fact]
    public void GenericFallback_NonMatchingSchema_ReasoningMatchesSpec()
    {
        var sut = new DeterministicNormalizationService(
        [
            new ValidationScenarioNormalizationRule(),
            new GenericNormalizationRule(),
        ]);

        var request = GenericRuleHelpers.MakeRequest("products",
        [
            GenericRuleHelpers.Col("sku",   PostgresType.Text, isCandidateKey: true),
            GenericRuleHelpers.Col("price", PostgresType.Numeric),
        ]);

        var result = sut.TryNormalize(request);

        Assert.True(result.Success);
        Assert.Equal(
            "Generic deterministic fallback: no domain-specific rule matched, " +
            "so the sheet is preserved as a single table.",
            result.Value!.Reasoning);
    }
}
