using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class SqlGeneratorServiceTests
{
    private readonly SqlGeneratorService _sut = new();

    // ── Structure ──────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateCreateTable_ContainsIfNotExists()
    {
        var sql = _sut.GenerateCreateTable(Schema("t", Col("id", PostgresType.Integer)));
        Assert.Contains("CREATE TABLE IF NOT EXISTS t", sql);
    }

    [Fact]
    public void GenerateCreateTable_EndsWithSemicolon()
    {
        var sql = _sut.GenerateCreateTable(Schema("t", Col("id", PostgresType.Integer)));
        Assert.EndsWith(");", sql.TrimEnd());
    }

    [Fact]
    public void GenerateCreateTable_TableNameAppearsInOutput()
    {
        var sql = _sut.GenerateCreateTable(Schema("order_items", Col("id", PostgresType.Integer)));
        Assert.Contains("order_items", sql);
    }

    // ── Type mapping ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PostgresType.Boolean,   "BOOLEAN")]
    [InlineData(PostgresType.Integer,   "INTEGER")]
    [InlineData(PostgresType.BigInt,    "BIGINT")]
    [InlineData(PostgresType.Numeric,   "NUMERIC")]
    [InlineData(PostgresType.Date,      "DATE")]
    [InlineData(PostgresType.Timestamp, "TIMESTAMP")]
    [InlineData(PostgresType.Text,      "TEXT")]
    public void GenerateCreateTable_TypeMapping_EmitsSqlKeyword(PostgresType type, string sqlKeyword)
    {
        var sql = _sut.GenerateCreateTable(Schema("t", Col("col", type)));
        Assert.Contains(sqlKeyword, sql);
    }

    // ── NOT NULL ───────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateCreateTable_NonNullableColumn_EmitsNotNull()
    {
        var sql = _sut.GenerateCreateTable(Schema("t", Col("name", PostgresType.Text, nullable: false)));
        Assert.Contains("NOT NULL", sql);
    }

    [Fact]
    public void GenerateCreateTable_NullableColumn_OmitsNotNull()
    {
        var sql = _sut.GenerateCreateTable(Schema("t", Col("name", PostgresType.Text, nullable: true)));
        Assert.DoesNotContain("NOT NULL", sql);
    }

    [Fact]
    public void GenerateCreateTable_MixedNullability_EachColumnCorrect()
    {
        var schema = Schema("t",
            Col("id",    PostgresType.Integer, nullable: false),
            Col("notes", PostgresType.Text,    nullable: true));

        var sql = _sut.GenerateCreateTable(schema);

        // id line has NOT NULL, notes line does not
        var lines = sql.Split('\n');
        var idLine    = lines.First(l => l.TrimStart().StartsWith("id"));
        var notesLine = lines.First(l => l.TrimStart().StartsWith("notes"));

        Assert.Contains("NOT NULL", idLine);
        Assert.DoesNotContain("NOT NULL", notesLine);
    }

    // ── PRIMARY KEY ────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateCreateTable_CandidateKey_EmitsPrimaryKey()
    {
        var sql = _sut.GenerateCreateTable(Schema("t", Col("id", PostgresType.Integer, candidateKey: true)));
        Assert.Contains("PRIMARY KEY (id)", sql);
    }

    [Fact]
    public void GenerateCreateTable_NoCandidateKey_OmitsPrimaryKey()
    {
        var sql = _sut.GenerateCreateTable(Schema("t", Col("name", PostgresType.Text)));
        Assert.DoesNotContain("PRIMARY KEY", sql);
    }

    [Fact]
    public void GenerateCreateTable_MultipleCandidateKeys_UsesFirstOnly()
    {
        var schema = Schema("t",
            Col("id",    PostgresType.Integer, candidateKey: true),
            Col("email", PostgresType.Text,    candidateKey: true));

        var sql = _sut.GenerateCreateTable(schema);

        Assert.Contains("PRIMARY KEY (id)", sql);
        Assert.DoesNotContain("PRIMARY KEY (email)", sql);
    }

    // ── Safety: no destructive SQL ─────────────────────────────────────────────

    [Fact]
    public void GenerateCreateTable_NeverContainsDropTable()
    {
        var sql = _sut.GenerateCreateTable(Schema("t", Col("id", PostgresType.Integer)));
        Assert.DoesNotContain("DROP",     sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE",   sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── Full output snapshot ───────────────────────────────────────────────────

    [Fact]
    public void GenerateCreateTable_FullSchema_ContainsAllExpectedFragments()
    {
        var schema = Schema("users",
            Col("id",         PostgresType.Integer,   nullable: false, candidateKey: true),
            Col("username",   PostgresType.Text,       nullable: false),
            Col("score",      PostgresType.Numeric,    nullable: true),
            Col("created_at", PostgresType.Timestamp,  nullable: false));

        var sql = _sut.GenerateCreateTable(schema);

        Assert.Contains("CREATE TABLE IF NOT EXISTS users", sql);
        Assert.Contains("id",           sql);
        Assert.Contains("INTEGER",      sql);
        Assert.Contains("username",     sql);
        Assert.Contains("TEXT",         sql);
        Assert.Contains("score",        sql);
        Assert.Contains("NUMERIC",      sql);
        Assert.Contains("created_at",   sql);
        Assert.Contains("TIMESTAMP",    sql);
        Assert.Contains("PRIMARY KEY (id)", sql);
    }

    // ── Edge case: no columns ──────────────────────────────────────────────────

    [Fact]
    public void GenerateCreateTable_EmptyColumns_StillReturnsValidSkeleton()
    {
        var schema = new TableSchema { TableName = "empty_table", SheetName = "s", Columns = [], SampleRowCount = 0 };
        var sql = _sut.GenerateCreateTable(schema);

        Assert.Contains("CREATE TABLE IF NOT EXISTS empty_table", sql);
        Assert.DoesNotContain("PRIMARY KEY", sql);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static TableSchema Schema(string name, params ColumnSchema[] columns) =>
        new() { TableName = name, SheetName = name, Columns = columns, SampleRowCount = 1 };

    private static ColumnSchema Col(
        string name,
        PostgresType type,
        bool nullable    = false,
        bool candidateKey = false) =>
        new()
        {
            Index         = 0,
            Name          = name,
            SnakeCaseName = name,
            InferredType  = type,
            IsNullable    = nullable,
            HasDuplicates = false,
            IsCandidateKey = candidateKey,
        };
}
