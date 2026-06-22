using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class SqlSeedParserServiceTests
{
    private readonly SqlSeedParserService _sut = new();

    // ── Table name ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsTableName()
    {
        var result = _sut.Parse(OneSeed("users", ["id"], [["1"]]));

        Assert.True(result.Success);
        Assert.Equal("users", result.Value!.TableName);
    }

    [Fact]
    public void Parse_SnakeCaseTableName_PreservedExactly()
    {
        var result = _sut.Parse(OneSeed("order_items", ["id"], [["1"]]));

        Assert.True(result.Success);
        Assert.Equal("order_items", result.Value!.TableName);
    }

    // ── Columns ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsSingleColumn()
    {
        var result = _sut.Parse(OneSeed("t", ["id"], [["1"]]));

        Assert.True(result.Success);
        Assert.Equal(["id"], result.Value!.Columns);
    }

    [Fact]
    public void Parse_ExtractsMultipleColumns()
    {
        var result = _sut.Parse(OneSeed("t", ["id", "username", "score"], [["1", "'Alice'", "9.5"]]));

        Assert.True(result.Success);
        Assert.Equal(["id", "username", "score"], result.Value!.Columns);
    }

    // ── Row count ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SingleRow_ReturnsOneRow()
    {
        var result = _sut.Parse(OneSeed("t", ["id"], [["1"]]));

        Assert.True(result.Success);
        Assert.Single(result.Value!.Rows);
    }

    [Fact]
    public void Parse_MultipleRows_AllReturned()
    {
        var result = _sut.Parse(OneSeed("t", ["id"], [["1"], ["2"], ["3"]]));

        Assert.True(result.Success);
        Assert.Equal(3, result.Value!.Rows.Count);
    }

    [Fact]
    public void Parse_MultipleRows_ValuesCorrectPerRow()
    {
        var result = _sut.Parse(OneSeed("t",
            ["id", "name"],
            [["1", "'Alice'"], ["2", "'Bob'"]]));

        Assert.True(result.Success);
        var rows = result.Value!.Rows;
        Assert.Equal("1",     rows[0][0]);
        Assert.Equal("Alice", rows[0][1]);
        Assert.Equal("2",     rows[1][0]);
        Assert.Equal("Bob",   rows[1][1]);
    }

    // ── NULL ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NullLiteral_ReturnsNullElement()
    {
        var result = _sut.Parse(OneSeed("t", ["name"], [["NULL"]]));

        Assert.True(result.Success);
        Assert.Null(result.Value!.Rows[0][0]);
    }

    [Fact]
    public void Parse_NullAmongOtherValues_OnlyNullIsNull()
    {
        var result = _sut.Parse(OneSeed("t",
            ["id", "name", "score"],
            [["1", "NULL", "9.5"]]));

        Assert.True(result.Success);
        var row = result.Value!.Rows[0];
        Assert.Equal("1",   row[0]);
        Assert.Null(row[1]);
        Assert.Equal("9.5", row[2]);
    }

    // ── Escaped quotes ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EscapedSingleQuote_UnescapedInResult()
    {
        var result = _sut.Parse(OneSeed("t", ["note"], [["'it''s fine'"]]));

        Assert.True(result.Success);
        Assert.Equal("it's fine", result.Value!.Rows[0][0]);
    }

    [Fact]
    public void Parse_MultipleEscapedQuotes_AllUnescaped()
    {
        var result = _sut.Parse(OneSeed("t", ["note"], [["'it''s the user''s data'"]]));

        Assert.True(result.Success);
        Assert.Equal("it's the user's data", result.Value!.Rows[0][0]);
    }

    [Fact]
    public void Parse_PlainString_ReturnedWithoutQuotes()
    {
        var result = _sut.Parse(OneSeed("t", ["name"], [["'Alice'"]]));

        Assert.True(result.Success);
        Assert.Equal("Alice", result.Value!.Rows[0][0]);
    }

    // ── Numbers ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Integer_PreservedAsString()
    {
        var result = _sut.Parse(OneSeed("t", ["age"], [["42"]]));

        Assert.True(result.Success);
        Assert.Equal("42", result.Value!.Rows[0][0]);
    }

    [Fact]
    public void Parse_NegativeInteger_PreservedAsString()
    {
        var result = _sut.Parse(OneSeed("t", ["balance"], [["-5"]]));

        Assert.True(result.Success);
        Assert.Equal("-5", result.Value!.Rows[0][0]);
    }

    [Fact]
    public void Parse_BigInt_PreservedAsString()
    {
        var result = _sut.Parse(OneSeed("t", ["big_id"], [["9999999999"]]));

        Assert.True(result.Success);
        Assert.Equal("9999999999", result.Value!.Rows[0][0]);
    }

    [Fact]
    public void Parse_Decimal_PreservedAsString()
    {
        var result = _sut.Parse(OneSeed("t", ["price"], [["3.14"]]));

        Assert.True(result.Success);
        Assert.Equal("3.14", result.Value!.Rows[0][0]);
    }

    // ── Booleans ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_TrueLiteral_PreservedAsString()
    {
        var result = _sut.Parse(OneSeed("t", ["active"], [["TRUE"]]));

        Assert.True(result.Success);
        Assert.Equal("TRUE", result.Value!.Rows[0][0]);
    }

    [Fact]
    public void Parse_FalseLiteral_PreservedAsString()
    {
        var result = _sut.Parse(OneSeed("t", ["active"], [["FALSE"]]));

        Assert.True(result.Success);
        Assert.Equal("FALSE", result.Value!.Rows[0][0]);
    }

    // ── ON CONFLICT DO NOTHING is optional ────────────────────────────────────

    [Fact]
    public void Parse_WithOnConflictDoNothing_ParsesSuccessfully()
    {
        const string sql =
            "INSERT INTO t (id)\nVALUES\n    (1)\nON CONFLICT DO NOTHING;";

        var result = _sut.Parse(sql);

        Assert.True(result.Success);
        Assert.Equal("t", result.Value!.TableName);
        Assert.Single(result.Value.Rows);
    }

    [Fact]
    public void Parse_WithoutOnConflictDoNothing_ParsesSuccessfully()
    {
        const string sql =
            "INSERT INTO t (id)\nVALUES\n    (1)";

        var result = _sut.Parse(sql);

        Assert.True(result.Success);
        Assert.Equal("t", result.Value!.TableName);
        Assert.Single(result.Value.Rows);
    }

    [Fact]
    public void Parse_WithSemicolonOnlyNoConflictClause_ParsesSuccessfully()
    {
        const string sql =
            "INSERT INTO t (id)\nVALUES\n    (1);";

        var result = _sut.Parse(sql);

        Assert.True(result.Success);
        Assert.Single(result.Value!.Rows);
    }

    // ── SQL comment header (as generated by the tool) ─────────────────────────

    [Fact]
    public void Parse_WithCommentHeaderLines_IgnoresComments()
    {
        const string sql =
            "-- Source  : /data/users.xlsx\n" +
            "-- Sheet   : Users\n" +
            "-- Table   : users\n" +
            "INSERT INTO users (id)\nVALUES\n    (1)\nON CONFLICT DO NOTHING;";

        var result = _sut.Parse(sql);

        Assert.True(result.Success);
        Assert.Equal("users", result.Value!.TableName);
    }

    // ── Error cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyInput_ReturnsFail()
    {
        var result = _sut.Parse(string.Empty);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsFail()
    {
        var result = _sut.Parse("   \n  ");

        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_CommentOnly_ReturnsFail()
    {
        // The no-rows comment produced by SeedSqlGeneratorService
        var result = _sut.Parse("-- No data rows found in sheet 'users'.");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_MissingValues_ReturnsFail()
    {
        const string sql = "INSERT INTO t (id)";

        var result = _sut.Parse(sql);

        Assert.False(result.Success);
        Assert.Contains("VALUES", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Full round-trip with the generator ────────────────────────────────────

    [Fact]
    public void Parse_SqlFromGenerator_RoundTripsCorrectly()
    {
        // Build SQL exactly as SeedSqlGeneratorService does
        const string sql =
            "INSERT INTO users (id, username, score)\n" +
            "VALUES\n" +
            "    (1, 'Alice', 9.5),\n" +
            "    (2, 'Bob', NULL)\n" +
            "ON CONFLICT DO NOTHING;";

        var result = _sut.Parse(sql);

        Assert.True(result.Success);
        var rec = result.Value!;

        Assert.Equal("users",                      rec.TableName);
        Assert.Equal(["id", "username", "score"],  rec.Columns);
        Assert.Equal(2,                            rec.Rows.Count);

        Assert.Equal("1",     rec.Rows[0][0]);
        Assert.Equal("Alice", rec.Rows[0][1]);
        Assert.Equal("9.5",   rec.Rows[0][2]);

        Assert.Equal("2",   rec.Rows[1][0]);
        Assert.Equal("Bob", rec.Rows[1][1]);
        Assert.Null(rec.Rows[1][2]);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    // Builds INSERT SQL in the exact format produced by SeedSqlGeneratorService.
    // Values in the rows array must already be SQL-formatted
    // (e.g. quoted strings as "'Alice'", unquoted numbers as "42", NULL as "NULL").
    private static string OneSeed(string table, string[] columns, string[][] rows)
    {
        var colList  = string.Join(", ", columns);
        var rowLines = rows.Select(r => $"    ({string.Join(", ", r)})").ToList();
        var body     = string.Join(",\n", rowLines);
        return $"INSERT INTO {table} ({colList})\nVALUES\n{body}\nON CONFLICT DO NOTHING;";
    }
}
