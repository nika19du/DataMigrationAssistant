using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

// ─── Shared fixtures ─────────────────────────────────────────────────────────

file static class SqlGenFixtures
{
    // Flat sheet: scenario_id, scenario_code, pay_element_type, employee_status
    // Row 1 & 2 share scenario_id=1 (parent deduplication case)
    public static SheetPreview SourceData() => new()
    {
        SheetName     = "validation_rules",
        FilePath      = "test.xlsx",
        Columns       =
        [
            new ColumnInfo { Index = 0, Name = "scenario_id",      SnakeCaseName = "scenario_id" },
            new ColumnInfo { Index = 1, Name = "scenario_code",    SnakeCaseName = "scenario_code" },
            new ColumnInfo { Index = 2, Name = "pay_element_type", SnakeCaseName = "pay_element_type" },
            new ColumnInfo { Index = 3, Name = "employee_status",  SnakeCaseName = "employee_status" },
        ],
        Rows          =
        [
            new Dictionary<string, string?> { ["scenario_id"] = "1", ["scenario_code"] = "GTN-01", ["pay_element_type"] = "BASIC",    ["employee_status"] = "ACTIVE" },
            new Dictionary<string, string?> { ["scenario_id"] = "1", ["scenario_code"] = "GTN-01", ["pay_element_type"] = "OVERTIME", ["employee_status"] = "ACTIVE" },
            new Dictionary<string, string?> { ["scenario_id"] = "2", ["scenario_code"] = "GTN-02", ["pay_element_type"] = "BASIC",    ["employee_status"] = "INACTIVE" },
        ],
        TotalRowCount = 3,
    };

    // Two-table proposal — parent + child with FK
    public static NormalizationProposal TwoTableProposal() => new()
    {
        Reasoning = "Sheet contains two distinct entities.",
        Tables    =
        [
            new ProposedTable
            {
                TableName     = "gtn_scenarios",
                Columns       =
                [
                    new ProposedColumn { Name = "id",            PostgresType = "INTEGER", IsNullable = false, IsPrimaryKey = true },
                    new ProposedColumn { Name = "scenario_code", PostgresType = "TEXT",    IsNullable = false },
                ],
                SourceColumns = ["scenario_id", "scenario_code"],
            },
            new ProposedTable
            {
                TableName     = "gtn_scenario_settings",
                Columns       =
                [
                    new ProposedColumn { Name = "id",               PostgresType = "INTEGER", IsNullable = false, IsPrimaryKey = true },
                    new ProposedColumn { Name = "scenario_id",      PostgresType = "INTEGER", IsNullable = false, ForeignKeyTo = "gtn_scenarios(id)" },
                    new ProposedColumn { Name = "pay_element_type", PostgresType = "TEXT",    IsNullable = false },
                    new ProposedColumn { Name = "employee_status",  PostgresType = "TEXT",    IsNullable = false },
                ],
                SourceColumns = ["pay_element_type", "employee_status"],
            },
        ],
    };

    public static SheetPreview EmptySourceData() => new()
    {
        SheetName     = "validation_rules",
        FilePath      = "test.xlsx",
        Columns       = SourceData().Columns,
        Rows          = [],
        TotalRowCount = 0,
    };
}

// ─── Happy path ───────────────────────────────────────────────────────────────

public class NormalizationSqlGeneratorServiceHappyPathTests
{
    private readonly INormalizationSqlGeneratorService _sut = new NormalizationSqlGeneratorService();

    [Fact]
    public void Generate_ValidTwoTableProposal_ReturnsSuccess()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.True(result.Success);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void Generate_ValidTwoTableProposal_ReturnsTwoTables()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Equal(2, result.Value!.Tables.Count);
    }

    [Fact]
    public void Generate_PreservesReasoning()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Equal("Sheet contains two distinct entities.", result.Value!.Reasoning);
    }

    [Fact]
    public void Generate_PreservesSourceColumns()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Contains("scenario_id", result.Value!.Tables[0].SourceColumns);
    }

    [Fact]
    public void Generate_SingleTableProposal_ReturnsSuccess()
    {
        var proposal = new NormalizationProposal
        {
            Reasoning = "Single entity.",
            Tables    =
            [
                new ProposedTable
                {
                    TableName     = "employees",
                    Columns       =
                    [
                        new ProposedColumn { Name = "employee_id", PostgresType = "INTEGER", IsPrimaryKey = true },
                        new ProposedColumn { Name = "name",        PostgresType = "TEXT",    IsNullable   = false },
                    ],
                    SourceColumns = ["employee_id", "name"],
                }
            ],
        };
        var source = new SheetPreview
        {
            SheetName = "employees", FilePath = "f.xlsx",
            Columns   = [
                new ColumnInfo { Index = 0, Name = "employee_id", SnakeCaseName = "employee_id" },
                new ColumnInfo { Index = 1, Name = "name",        SnakeCaseName = "name" },
            ],
            Rows      = [new Dictionary<string, string?> { ["employee_id"] = "1", ["name"] = "Alice" }],
        };

        var result = _sut.Generate(proposal, source);
        Assert.True(result.Success);
        Assert.Single(result.Value!.Tables);
    }
}

// ─── CREATE TABLE SQL ─────────────────────────────────────────────────────────

public class NormalizationSqlGeneratorCreateTableTests
{
    private readonly INormalizationSqlGeneratorService _sut = new NormalizationSqlGeneratorService();

    [Fact]
    public void Generate_ParentCreateTableSql_ContainsTableName()
    {
        var result    = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var parentSql = result.Value!.Tables[0].CreateTableSql;
        Assert.Contains("gtn_scenarios", parentSql);
    }

    [Fact]
    public void Generate_ChildCreateTableSql_ContainsTableName()
    {
        var result   = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var childSql = result.Value!.Tables[1].CreateTableSql;
        Assert.Contains("gtn_scenario_settings", childSql);
    }

    [Fact]
    public void Generate_CreateTableSql_UsesCreateTableIfNotExists()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        foreach (var table in result.Value!.Tables)
            Assert.Contains("CREATE TABLE IF NOT EXISTS", table.CreateTableSql);
    }

    [Fact]
    public void Generate_ParentCreateTableSql_ContainsPrimaryKeyConstraint()
    {
        var result    = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var parentSql = result.Value!.Tables[0].CreateTableSql;
        Assert.Contains("PRIMARY KEY (id)", parentSql);
    }

    [Fact]
    public void Generate_ChildCreateTableSql_ContainsForeignKeyConstraint()
    {
        var result   = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var childSql = result.Value!.Tables[1].CreateTableSql;
        Assert.Contains("FOREIGN KEY (scenario_id) REFERENCES gtn_scenarios(id)", childSql);
    }

    [Fact]
    public void Generate_ChildCreateTableSql_ContainsForeignKeyColumn()
    {
        var result   = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var childSql = result.Value!.Tables[1].CreateTableSql;
        Assert.Contains("scenario_id", childSql);
    }

    [Fact]
    public void Generate_CreateTableSql_ContainsColumnTypes()
    {
        var result    = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var parentSql = result.Value!.Tables[0].CreateTableSql;
        Assert.Contains("INTEGER", parentSql);
        Assert.Contains("TEXT",    parentSql);
    }

    [Fact]
    public void Generate_CreateTableSql_NotNullColumnIncludesNotNull()
    {
        var result    = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var parentSql = result.Value!.Tables[0].CreateTableSql;
        Assert.Contains("NOT NULL", parentSql);
    }

    [Fact]
    public void Generate_CreateTableSql_NullableColumnOmitsNotNull()
    {
        var proposal = new NormalizationProposal
        {
            Reasoning = "x",
            Tables    =
            [
                new ProposedTable
                {
                    TableName     = "t",
                    Columns       =
                    [
                        new ProposedColumn { Name = "id",          PostgresType = "INTEGER", IsPrimaryKey = true  },
                        new ProposedColumn { Name = "description", PostgresType = "TEXT",    IsNullable   = true  },
                    ],
                    SourceColumns = ["id"],
                }
            ],
        };
        var source = new SheetPreview
        {
            Columns = [new ColumnInfo { Index = 0, Name = "id", SnakeCaseName = "id" }],
            Rows    = [],
        };

        var result = _sut.Generate(proposal, source);
        var sql    = result.Value!.Tables[0].CreateTableSql;
        // "description TEXT" should NOT be followed by NOT NULL
        Assert.DoesNotContain("description TEXT NOT NULL", sql);
        Assert.Contains("description TEXT", sql);
    }

    [Fact]
    public void Generate_NoDestructiveSqlInCreateTable()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var allSql = result.Value!.CombinedMigrationSql;
        Assert.DoesNotContain("DROP ",     allSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ",   allSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE ", allSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_NoDestructiveSqlInSeed()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var allSql = result.Value!.CombinedSeedSql;
        Assert.DoesNotContain("DROP ",     allSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ",   allSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE ", allSql, StringComparison.OrdinalIgnoreCase);
    }
}

// ─── Seed SQL ─────────────────────────────────────────────────────────────────

public class NormalizationSqlGeneratorSeedTests
{
    private readonly INormalizationSqlGeneratorService _sut = new NormalizationSqlGeneratorService();

    [Fact]
    public void Generate_SeedSql_ContainsInsertInto()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        foreach (var table in result.Value!.Tables)
            Assert.Contains("INSERT INTO", table.SeedSql);
    }

    [Fact]
    public void Generate_SeedSql_HasOnConflictDoNothing()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        foreach (var table in result.Value!.Tables)
            Assert.Contains("ON CONFLICT DO NOTHING", table.SeedSql);
    }

    [Fact]
    public void Generate_ParentSeedSql_DeduplicatesRowsByPK()
    {
        // Rows 1 & 2 both have scenario_id=1 — parent should emit only one row for GTN-01
        var result     = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var parentSeed = result.Value!.Tables[0].SeedSql;

        // 'GTN-01' appears exactly once (deduplication worked)
        Assert.Equal(1, parentSeed.OccurrencesOf("'GTN-01'"));
        // 'GTN-02' appears exactly once
        Assert.Equal(1, parentSeed.OccurrencesOf("'GTN-02'"));
    }

    [Fact]
    public void Generate_ParentSeedSql_HasTwoValueRows()
    {
        var result     = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var parentSeed = result.Value!.Tables[0].SeedSql;
        // Two unique scenario IDs → two VALUE rows
        Assert.Equal(2, parentSeed.OccurrencesOf("    ("));
    }

    [Fact]
    public void Generate_ChildSeedSql_HasThreeValueRows()
    {
        // 3 source rows → 3 child rows (child has no deduplication)
        var result    = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var childSeed = result.Value!.Tables[1].SeedSql;
        Assert.Equal(3, childSeed.OccurrencesOf("    ("));
    }

    [Fact]
    public void Generate_ChildSeedSql_IncludesFKValues()
    {
        var result    = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var childSeed = result.Value!.Tables[1].SeedSql;
        // Scenario IDs 1 and 2 must appear as FK values in the child seed
        Assert.Contains("1", childSeed);
        Assert.Contains("2", childSeed);
    }

    [Fact]
    public void Generate_ChildSeedSql_IncludesSourceColumnValues()
    {
        var result    = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var childSeed = result.Value!.Tables[1].SeedSql;
        Assert.Contains("'BASIC'",    childSeed);
        Assert.Contains("'OVERTIME'", childSeed);
        Assert.Contains("'INACTIVE'", childSeed);
    }

    [Fact]
    public void Generate_ChildSeedSql_AutoSequencePkIsUnique()
    {
        // Auto-sequence PKs should be 1, 2, 3 for child rows
        var result    = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var childSeed = result.Value!.Tables[1].SeedSql;
        // Sequence starts at 1; all three rows should appear
        Assert.Contains("(1,", childSeed);
        Assert.Contains("(2,", childSeed);
        Assert.Contains("(3,", childSeed);
    }

    [Fact]
    public void Generate_NullCellValue_EmittedAsNull()
    {
        var proposal = new NormalizationProposal
        {
            Reasoning = "x",
            Tables    =
            [
                new ProposedTable
                {
                    TableName     = "employees",
                    Columns       =
                    [
                        new ProposedColumn { Name = "id",   PostgresType = "INTEGER", IsPrimaryKey = true },
                        new ProposedColumn { Name = "note", PostgresType = "TEXT",    IsNullable   = true },
                    ],
                    SourceColumns = ["id", "note"],
                }
            ],
        };
        var source = new SheetPreview
        {
            Columns = [
                new ColumnInfo { Index = 0, Name = "id",   SnakeCaseName = "id" },
                new ColumnInfo { Index = 1, Name = "note", SnakeCaseName = "note" },
            ],
            Rows    = [new Dictionary<string, string?> { ["id"] = "1", ["note"] = null }],
        };

        var result = _sut.Generate(proposal, source);
        Assert.True(result.Success);
        Assert.Contains("NULL", result.Value!.Tables[0].SeedSql);
    }

    [Fact]
    public void Generate_EmptySourceRows_SeedSqlIsComment()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.EmptySourceData());
        Assert.True(result.Success);
        foreach (var table in result.Value!.Tables)
            Assert.StartsWith("--", table.SeedSql.TrimStart());
    }

    [Fact]
    public void Generate_EmptySourceRows_StillGeneratesCreateTable()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.EmptySourceData());
        Assert.True(result.Success);
        foreach (var table in result.Value!.Tables)
            Assert.Contains("CREATE TABLE IF NOT EXISTS", table.CreateTableSql);
    }

    [Fact]
    public void Generate_StringValuesAreSingleQuoted()
    {
        var result     = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var parentSeed = result.Value!.Tables[0].SeedSql;
        Assert.Contains("'GTN-01'", parentSeed);
    }

    [Fact]
    public void Generate_IntegerValuesAreUnquoted()
    {
        var result     = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var parentSeed = result.Value!.Tables[0].SeedSql;
        // PK value "1" should appear unquoted (not '1')
        Assert.Contains("1,", parentSeed);
        Assert.DoesNotContain("'1'", parentSeed);
    }
}

// ─── Validation failures ──────────────────────────────────────────────────────

public class NormalizationSqlGeneratorValidationTests
{
    private readonly INormalizationSqlGeneratorService _sut = new NormalizationSqlGeneratorService();

    [Fact]
    public void Generate_MissingSourceColumn_ReturnsFailure()
    {
        var proposal = WithModifiedParentTable(SqlGenFixtures.TwoTableProposal(),
            t => new ProposedTable
            {
                TableName     = t.TableName,
                Columns       = t.Columns,
                SourceColumns = ["nonexistent_column"],
            });

        var result = _sut.Generate(proposal, SqlGenFixtures.SourceData());
        Assert.False(result.Success);
        Assert.Contains("nonexistent_column", result.Error!);
    }

    [Fact]
    public void Generate_NoPrimaryKey_ReturnsFailure()
    {
        var proposal = WithModifiedParentTable(SqlGenFixtures.TwoTableProposal(),
            t => new ProposedTable
            {
                TableName     = t.TableName,
                Columns       = t.Columns.Select(c => new ProposedColumn
                {
                    Name         = c.Name,
                    PostgresType = c.PostgresType,
                    IsNullable   = c.IsNullable,
                    IsPrimaryKey = false,          // strip all PKs
                    ForeignKeyTo = c.ForeignKeyTo,
                }).ToList(),
                SourceColumns = t.SourceColumns,
            });

        var result = _sut.Generate(proposal, SqlGenFixtures.SourceData());
        Assert.False(result.Success);
        Assert.Contains("no primary key", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_MultiplePrimaryKeys_ReturnsFailure()
    {
        var proposal = WithModifiedParentTable(SqlGenFixtures.TwoTableProposal(),
            t => new ProposedTable
            {
                TableName     = t.TableName,
                Columns       =
                [
                    new ProposedColumn { Name = "id1", PostgresType = "INTEGER", IsPrimaryKey = true },
                    new ProposedColumn { Name = "id2", PostgresType = "INTEGER", IsPrimaryKey = true },
                ],
                SourceColumns = t.SourceColumns,
            });

        var result = _sut.Generate(proposal, SqlGenFixtures.SourceData());
        Assert.False(result.Success);
        Assert.Contains("more than one primary key", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_EmptyTablesProposal_ReturnsFailure()
    {
        var proposal = new NormalizationProposal { Reasoning = "no tables", Tables = [] };
        var result   = _sut.Generate(proposal, SqlGenFixtures.SourceData());
        Assert.False(result.Success);
        Assert.Contains("no tables", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_EmptyTableName_ReturnsFailure()
    {
        var proposal = WithModifiedParentTable(SqlGenFixtures.TwoTableProposal(),
            t => new ProposedTable
            {
                TableName     = string.Empty,
                Columns       = t.Columns,
                SourceColumns = t.SourceColumns,
            });

        var result = _sut.Generate(proposal, SqlGenFixtures.SourceData());
        Assert.False(result.Success);
        Assert.Contains("empty name", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WhitespaceTableName_ReturnsFailure()
    {
        var proposal = WithModifiedParentTable(SqlGenFixtures.TwoTableProposal(),
            t => new ProposedTable
            {
                TableName     = "   ",
                Columns       = t.Columns,
                SourceColumns = t.SourceColumns,
            });

        var result = _sut.Generate(proposal, SqlGenFixtures.SourceData());
        Assert.False(result.Success);
        Assert.Contains("empty name", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_EmptyColumnName_ReturnsFailure()
    {
        var proposal = WithModifiedParentTable(SqlGenFixtures.TwoTableProposal(),
            t => new ProposedTable
            {
                TableName     = t.TableName,
                Columns       = [new ProposedColumn { Name = string.Empty, PostgresType = "INTEGER", IsPrimaryKey = true }],
                SourceColumns = t.SourceColumns,
            });

        var result = _sut.Generate(proposal, SqlGenFixtures.SourceData());
        Assert.False(result.Success);
        Assert.Contains("empty name", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_InvalidFKTargetTable_ReturnsFailure()
    {
        var tables = SqlGenFixtures.TwoTableProposal().Tables.ToList();
        tables[1] = new ProposedTable
        {
            TableName     = tables[1].TableName,
            Columns       = tables[1].Columns.Select(c => new ProposedColumn
            {
                Name         = c.Name,
                PostgresType = c.PostgresType,
                IsNullable   = c.IsNullable,
                IsPrimaryKey = c.IsPrimaryKey,
                ForeignKeyTo = c.ForeignKeyTo is not null ? "nonexistent_table(id)" : null,
            }).ToList(),
            SourceColumns = tables[1].SourceColumns,
        };
        var proposal = new NormalizationProposal
        {
            Reasoning = "x",
            Tables    = tables,
        };

        var result = _sut.Generate(proposal, SqlGenFixtures.SourceData());
        Assert.False(result.Success);
        Assert.Contains("nonexistent_table", result.Error!);
    }

    [Fact]
    public void Generate_ErrorMessageIdentifiesOffendingTable()
    {
        var proposal = WithModifiedParentTable(SqlGenFixtures.TwoTableProposal(),
            t => new ProposedTable
            {
                TableName     = t.TableName,
                Columns       = t.Columns.Select(c => new ProposedColumn
                {
                    Name         = c.Name,
                    PostgresType = c.PostgresType,
                    IsNullable   = c.IsNullable,
                    IsPrimaryKey = false,
                    ForeignKeyTo = c.ForeignKeyTo,
                }).ToList(),
                SourceColumns = t.SourceColumns,
            });

        var result = _sut.Generate(proposal, SqlGenFixtures.SourceData());
        Assert.False(result.Success);
        // Error should name the table
        Assert.Contains("gtn_scenarios", result.Error!);
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static NormalizationProposal WithModifiedParentTable(
        NormalizationProposal original,
        Func<ProposedTable, ProposedTable> modify)
    {
        var tables = original.Tables.ToList();
        tables[0] = modify(tables[0]);
        return new NormalizationProposal { Reasoning = original.Reasoning, Tables = tables };
    }
}

// ─── Combined output ─────────────────────────────────────────────────────────

public class NormalizationSqlGeneratorCombinedTests
{
    private readonly INormalizationSqlGeneratorService _sut = new NormalizationSqlGeneratorService();

    [Fact]
    public void Generate_CombinedMigrationSql_ContainsBothCreateTables()
    {
        var result   = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var combined = result.Value!.CombinedMigrationSql;
        Assert.Contains("gtn_scenarios",         combined);
        Assert.Contains("gtn_scenario_settings", combined);
    }

    [Fact]
    public void Generate_CombinedSeedSql_ContainsBothInserts()
    {
        var result   = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var combined = result.Value!.CombinedSeedSql;
        Assert.Contains("INSERT INTO gtn_scenarios",         combined);
        Assert.Contains("INSERT INTO gtn_scenario_settings", combined);
    }

    [Fact]
    public void Generate_CombinedMigrationSql_IsNotEmpty()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.NotEmpty(result.Value!.CombinedMigrationSql);
    }

    [Fact]
    public void Generate_CombinedSeedSql_IsNotEmpty()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.NotEmpty(result.Value!.CombinedSeedSql);
    }
}

// ─── Markdown report ─────────────────────────────────────────────────────────

public class NormalizationSqlGeneratorMarkdownTests
{
    private readonly INormalizationSqlGeneratorService _sut = new NormalizationSqlGeneratorService();

    [Fact]
    public void Generate_MarkdownReport_ContainsReasoning()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Contains("Sheet contains two distinct entities.", result.Value!.MarkdownReport);
    }

    [Fact]
    public void Generate_MarkdownReport_ContainsParentTableName()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Contains("gtn_scenarios", result.Value!.MarkdownReport);
    }

    [Fact]
    public void Generate_MarkdownReport_ContainsChildTableName()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Contains("gtn_scenario_settings", result.Value!.MarkdownReport);
    }

    [Fact]
    public void Generate_MarkdownReport_ContainsRelationshipsSection()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Contains("Relationships", result.Value!.MarkdownReport);
    }

    [Fact]
    public void Generate_MarkdownReport_ContainsFKRelationship()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Contains("gtn_scenarios(id)", result.Value!.MarkdownReport);
    }

    [Fact]
    public void Generate_MarkdownReport_ContainsSourceColumnMappingSection()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Contains("Source Column Mapping", result.Value!.MarkdownReport);
    }

    [Fact]
    public void Generate_MarkdownReport_ContainsParentSourceColumns()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        var md     = result.Value!.MarkdownReport;
        Assert.Contains("scenario_id",   md);
        Assert.Contains("scenario_code", md);
    }

    [Fact]
    public void Generate_MarkdownReport_ContainsChildSourceColumns()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Contains("pay_element_type", result.Value!.MarkdownReport);
    }

    [Fact]
    public void Generate_MarkdownReport_ContainsColumnTable()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        // Markdown table header
        Assert.Contains("| Column |", result.Value!.MarkdownReport);
    }

    [Fact]
    public void Generate_MarkdownReport_ContainsPrimaryKeyMarker()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.Contains("Yes", result.Value!.MarkdownReport);  // IsPrimaryKey = Yes
    }

    [Fact]
    public void Generate_MarkdownReport_IsNotEmpty()
    {
        var result = _sut.Generate(SqlGenFixtures.TwoTableProposal(), SqlGenFixtures.SourceData());
        Assert.NotEmpty(result.Value!.MarkdownReport);
    }
}

// ─── Shared count helper ──────────────────────────────────────────────────────

file static class CountHelper
{
    public static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}

// xUnit extension to allow calling CountOccurrences from tests
file static class StringExtensions
{
    public static int OccurrencesOf(this string text, string pattern)
        => CountHelper.CountOccurrences(text, pattern);
}
