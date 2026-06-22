using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public class NormalizationRequestModelTests
{
    [Fact]
    public void NormalizationRequest_DefaultsAreNotNull()
    {
        var request = new NormalizationRequest();
        Assert.NotNull(request.SheetPreview);
        Assert.NotNull(request.FlatSchema);
    }

    [Fact]
    public void NormalizationRequest_StoresSheetPreview()
    {
        var preview = new SheetPreview { SheetName = "Validation Rules" };
        var request = new NormalizationRequest { SheetPreview = preview };
        Assert.Equal("Validation Rules", request.SheetPreview.SheetName);
    }

    [Fact]
    public void NormalizationRequest_StoresFlatSchema()
    {
        var schema = new TableSchema { TableName = "validation_rules" };
        var request = new NormalizationRequest { FlatSchema = schema };
        Assert.Equal("validation_rules", request.FlatSchema.TableName);
    }
}

public class ProposedColumnModelTests
{
    [Fact]
    public void ProposedColumn_DefaultsAreEmpty()
    {
        var col = new ProposedColumn();
        Assert.Equal(string.Empty, col.Name);
        Assert.Equal(string.Empty, col.PostgresType);
        Assert.False(col.IsNullable);
        Assert.False(col.IsPrimaryKey);
        Assert.Null(col.ForeignKeyTo);
    }

    [Fact]
    public void ProposedColumn_PrimaryKey_StoredCorrectly()
    {
        var col = new ProposedColumn { Name = "id", PostgresType = "INTEGER", IsPrimaryKey = true };
        Assert.Equal("id", col.Name);
        Assert.Equal("INTEGER", col.PostgresType);
        Assert.True(col.IsPrimaryKey);
    }

    [Fact]
    public void ProposedColumn_ForeignKey_StoredCorrectly()
    {
        var col = new ProposedColumn
        {
            Name = "scenario_id",
            PostgresType = "INTEGER",
            IsNullable = false,
            ForeignKeyTo = "gtn_scenarios(id)",
        };
        Assert.Equal("gtn_scenarios(id)", col.ForeignKeyTo);
        Assert.False(col.IsNullable);
    }

    [Fact]
    public void ProposedColumn_NullableForeignKey_IsNull()
    {
        var col = new ProposedColumn { Name = "x" };
        Assert.Null(col.ForeignKeyTo);
    }
}

public class ProposedTableModelTests
{
    [Fact]
    public void ProposedTable_DefaultsAreEmpty()
    {
        var table = new ProposedTable();
        Assert.Equal(string.Empty, table.TableName);
        Assert.Empty(table.Columns);
        Assert.Empty(table.SourceColumns);
        Assert.Equal(string.Empty, table.CreateTableSql);
        Assert.Equal(string.Empty, table.SeedSql);
    }

    [Fact]
    public void ProposedTable_StoresColumnsAndSourceColumns()
    {
        var col = new ProposedColumn { Name = "id", PostgresType = "INTEGER", IsPrimaryKey = true };
        var table = new ProposedTable
        {
            TableName = "gtn_scenarios",
            Columns = [col],
            SourceColumns = ["scenario_id", "scenario_code"],
            CreateTableSql = "CREATE TABLE gtn_scenarios (id INTEGER PRIMARY KEY);",
            SeedSql = "INSERT INTO gtn_scenarios (id) VALUES (1) ON CONFLICT DO NOTHING;",
        };

        Assert.Equal("gtn_scenarios", table.TableName);
        Assert.Single(table.Columns);
        Assert.Equal("id", table.Columns[0].Name);
        Assert.Equal(2, table.SourceColumns.Count);
        Assert.Contains("CREATE TABLE", table.CreateTableSql);
        Assert.Contains("ON CONFLICT DO NOTHING", table.SeedSql);
    }
}

public class NormalizationProposalModelTests
{
    [Fact]
    public void NormalizationProposal_DefaultsAreEmpty()
    {
        var proposal = new NormalizationProposal();
        Assert.Equal(string.Empty, proposal.Reasoning);
        Assert.Empty(proposal.Tables);
        Assert.Equal(string.Empty, proposal.CombinedMigrationSql);
        Assert.Equal(string.Empty, proposal.CombinedSeedSql);
        Assert.Equal(string.Empty, proposal.MarkdownReport);
    }

    [Fact]
    public void NormalizationProposal_StoresTwoTables()
    {
        var proposal = new NormalizationProposal
        {
            Reasoning = "Sheet mixes scenario identity with settings.",
            Tables =
            [
                new ProposedTable { TableName = "gtn_scenarios" },
                new ProposedTable { TableName = "gtn_scenario_settings" },
            ],
            CombinedMigrationSql = "CREATE TABLE gtn_scenarios ...;\nCREATE TABLE gtn_scenario_settings ...;",
            CombinedSeedSql = "INSERT INTO gtn_scenarios ...;\nINSERT INTO gtn_scenario_settings ...;",
            MarkdownReport = "## Normalization Proposal\n...",
        };

        Assert.Equal(2, proposal.Tables.Count);
        Assert.Equal("gtn_scenarios", proposal.Tables[0].TableName);
        Assert.Equal("gtn_scenario_settings", proposal.Tables[1].TableName);
        Assert.Contains("Sheet mixes scenario identity", proposal.Reasoning);
        Assert.Contains("## Normalization Proposal", proposal.MarkdownReport);
    }
}

public class NullNormalizationServiceTests
{
    private readonly INormalizationProposalService _sut = new NullNormalizationService();

    [Fact]
    public async Task ProposeAsync_ReturnsFailure()
    {
        var result = await _sut.ProposeAsync(new NormalizationRequest());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ProposeAsync_ReturnsExpectedErrorMessage()
    {
        var result = await _sut.ProposeAsync(new NormalizationRequest());
        Assert.Equal("No AI normalization provider configured.", result.Error);
    }

    [Fact]
    public async Task ProposeAsync_ValueIsDefault()
    {
        var result = await _sut.ProposeAsync(new NormalizationRequest());
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ProposeAsync_WithCancellationToken_ReturnsFailure()
    {
        using var cts = new CancellationTokenSource();
        var result = await _sut.ProposeAsync(new NormalizationRequest(), cts.Token);
        Assert.False(result.Success);
    }
}

public class NormalizationServiceFactoryTests
{
    private readonly NormalizationServiceFactory _sut = new(
        new NullNormalizationService(),
        new ClaudeNormalizationService(null),
        new OllamaNormalizationService(new HttpClient()));

    [Fact]
    public void Create_NullProvider_ReturnsNullService()
    {
        var service = _sut.Create(null);
        Assert.IsType<NullNormalizationService>(service);
    }

    [Fact]
    public void Create_EmptyProvider_ReturnsNullService()
    {
        var service = _sut.Create(string.Empty);
        Assert.IsType<NullNormalizationService>(service);
    }

    [Fact]
    public void Create_UnknownProvider_ReturnsNullService()
    {
        var service = _sut.Create("openai");
        Assert.IsType<NullNormalizationService>(service);
    }

    [Fact]
    public void Create_ClaudeProvider_ReturnsClaudeNormalizationService()
    {
        var service = _sut.Create("claude");
        Assert.IsType<ClaudeNormalizationService>(service);
    }

    [Fact]
    public void Create_OllamaProvider_ReturnsOllamaNormalizationService()
    {
        var service = _sut.Create("ollama");
        Assert.IsType<OllamaNormalizationService>(service);
    }

    [Fact]
    public void Create_ProviderNameIsCaseInsensitive()
    {
        Assert.IsType<ClaudeNormalizationService>(_sut.Create("CLAUDE"));
        Assert.IsType<ClaudeNormalizationService>(_sut.Create("Claude"));
        Assert.IsType<OllamaNormalizationService>(_sut.Create("OLLAMA"));
        Assert.IsType<OllamaNormalizationService>(_sut.Create("Ollama"));
    }
}
