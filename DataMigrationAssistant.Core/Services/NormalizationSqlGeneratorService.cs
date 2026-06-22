using DataMigrationAssistant.Core.Generators;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;
using System.Text;

namespace DataMigrationAssistant.Core.Services;

public sealed class NormalizationSqlGeneratorService : INormalizationSqlGeneratorService
{
    public ServiceResult<NormalizationProposal> Generate(
        NormalizationProposal proposal,
        SheetPreview sourceData)
    {
        var validationError = Validate(proposal, sourceData);
        if (validationError is not null)
            return ServiceResult<NormalizationProposal>.Fail(validationError);

        var enrichedTables = new List<ProposedTable>(proposal.Tables.Count);
        foreach (var table in proposal.Tables)
        {
            var createSql = GenerateCreateTable(table);
            var (seedSql, seedError) = GenerateSeedSql(table, sourceData);
            if (seedError is not null)
                return ServiceResult<NormalizationProposal>.Fail(seedError);

            enrichedTables.Add(new ProposedTable
            {
                TableName      = table.TableName,
                Columns        = table.Columns,
                SourceColumns  = table.SourceColumns,
                CreateTableSql = createSql,
                SeedSql        = seedSql,
            });
        }

        var combinedMigration = string.Join("\n\n", enrichedTables.Select(t => t.CreateTableSql));
        var combinedSeed      = string.Join("\n\n", enrichedTables.Select(t => t.SeedSql));
        var markdownReport    = BuildMarkdownReport(proposal.Reasoning, enrichedTables);

        return ServiceResult<NormalizationProposal>.Ok(new NormalizationProposal
        {
            Reasoning            = proposal.Reasoning,
            Tables               = enrichedTables,
            CombinedMigrationSql = combinedMigration,
            CombinedSeedSql      = combinedSeed,
            MarkdownReport       = markdownReport,
        });
    }

    // ── Validation ───────────────────────────────────────────────────────────

    private static string? Validate(NormalizationProposal proposal, SheetPreview sourceData)
    {
        if (proposal.Tables.Count == 0)
            return "The normalization proposal contains no tables. The AI provider returned an empty or incomplete response.";

        var sheetColumnNames = sourceData.Columns
            .Select(c => c.SnakeCaseName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tableNames = proposal.Tables
            .Where(t => !string.IsNullOrWhiteSpace(t.TableName))
            .Select(t => t.TableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var table in proposal.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.TableName))
                return "A proposed table has an empty name.";

            foreach (var col in table.Columns)
            {
                if (string.IsNullOrWhiteSpace(col.Name))
                    return $"Table '{table.TableName}' has a column with an empty name.";
            }

            var pkCount = table.Columns.Count(c => c.IsPrimaryKey);
            if (pkCount == 0)
                return $"Table '{table.TableName}' has no primary key column.";
            if (pkCount > 1)
                return $"Table '{table.TableName}' has more than one primary key column.";

            foreach (var srcCol in table.SourceColumns)
            {
                if (!sheetColumnNames.Contains(srcCol))
                    return $"Table '{table.TableName}' references source column '{srcCol}' which does not exist in the sheet.";
            }

            foreach (var col in table.Columns.Where(c => c.ForeignKeyTo is not null))
            {
                var targetTable = ParseFkTableName(col.ForeignKeyTo!);
                if (!tableNames.Contains(targetTable))
                    return $"Column '{col.Name}' in table '{table.TableName}' references '{col.ForeignKeyTo}', " +
                           $"but table '{targetTable}' does not exist in the proposal.";
            }
        }

        return null;
    }

    // ── CREATE TABLE ─────────────────────────────────────────────────────────

    private static string GenerateCreateTable(ProposedTable table)
    {
        var pk      = table.Columns.First(c => c.IsPrimaryKey);
        var fkCols  = table.Columns.Where(c => c.ForeignKeyTo is not null).ToList();
        var lines   = new List<string>(table.Columns.Count + 1 + fkCols.Count);

        foreach (var col in table.Columns)
        {
            var nullability = col.IsNullable ? string.Empty : " NOT NULL";
            lines.Add($"    {col.Name} {col.PostgresType}{nullability}");
        }

        lines.Add($"    PRIMARY KEY ({pk.Name})");

        foreach (var fkCol in fkCols)
        {
            var (refTable, refCol) = ParseFkTarget(fkCol.ForeignKeyTo!);
            lines.Add($"    FOREIGN KEY ({fkCol.Name}) REFERENCES {refTable}({refCol})");
        }

        return $"CREATE TABLE IF NOT EXISTS {table.TableName} (\n{string.Join(",\n", lines)}\n);";
    }

    // ── Seed SQL ─────────────────────────────────────────────────────────────

    private static (string sql, string? error) GenerateSeedSql(
        ProposedTable table,
        SheetPreview sourceData)
    {
        if (sourceData.Rows.Count == 0)
            return ($"-- No data rows found for table '{table.TableName}'.", null);

        var columnList   = string.Join(", ", table.Columns.Select(c => c.Name));
        var autoSeq      = table.SeedSequenceStart;
        var rowValues    = new List<List<string>>(sourceData.Rows.Count);
        var seenPkValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sourceData.Rows)
        {
            var values   = new List<string>(table.Columns.Count);
            var skip     = false;
            string? rowError = null;

            foreach (var col in table.Columns)
            {
                var (val, err) = ResolveColumnValue(col, table.SourceColumns, row, ref autoSeq);
                if (err is not null) { rowError = err; break; }

                if (col.IsPrimaryKey && !seenPkValues.Add(val))
                {
                    skip = true;
                    break;
                }

                values.Add(val);
            }

            if (rowError is not null) return (string.Empty, rowError);
            if (!skip) rowValues.Add(values);
        }

        if (rowValues.Count == 0)
            return ($"-- No rows generated for table '{table.TableName}'.", null);

        var valueLines = rowValues.Select(r => $"    ({string.Join(", ", r)})");
        var sql = $"INSERT INTO {table.TableName} ({columnList})\nVALUES\n{string.Join(",\n", valueLines)}\nON CONFLICT DO NOTHING;";
        return (sql, null);
    }

    // ── Column value resolution ───────────────────────────────────────────────
    //
    // Priority:
    //   1. Exact column name match in source row (covers FK cols and renamed cols with same name)
    //   2. Suffix match in source_columns list: "scenario_id" → proposed "id" via "_id" suffix
    //   3. Auto-sequence integer for PK columns with no source
    //   4. NULL for nullable columns
    //   5. Error for FK columns whose name doesn't exist in the source row
    //   6. Error for non-nullable non-PK columns with no source

    private static (string value, string? error) ResolveColumnValue(
        ProposedColumn col,
        IReadOnlyList<string> tableSourceColumns,
        IReadOnlyDictionary<string, string?> row,
        ref int autoSeq)
    {
        // 1. Exact name match in source row
        if (row.TryGetValue(col.Name, out var exactVal))
            return (FormatValue(exactVal, col.PostgresType), null);

        // 2. Suffix match: source "scenario_id" → proposed "id"
        var suffixPattern = "_" + col.Name;
        var suffixMatch   = tableSourceColumns.FirstOrDefault(
            s => s.EndsWith(suffixPattern, StringComparison.OrdinalIgnoreCase));
        if (suffixMatch is not null && row.TryGetValue(suffixMatch, out var suffixVal))
            return (FormatValue(suffixVal, col.PostgresType), null);

        // 3. Auto-sequence for PK with no source (child surrogate keys)
        if (col.IsPrimaryKey)
            return ((autoSeq++).ToString(), null);

        // 4. Nullable column with no source
        if (col.IsNullable)
            return ("NULL", null);

        // 5. FK column whose name is not in the sheet
        if (col.ForeignKeyTo is not null)
            return (string.Empty,
                $"Cannot derive value for foreign key column '{col.Name}': " +
                $"no source column '{col.Name}' found in sheet.");

        // 6. Non-nullable, non-PK, no source
        return (string.Empty,
            $"Cannot derive value for non-nullable column '{col.Name}': " +
            $"no matching source column found.");
    }

    // ── Markdown report ───────────────────────────────────────────────────────

    private static string BuildMarkdownReport(string reasoning, IReadOnlyList<ProposedTable> tables)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Normalization Proposal");
        sb.AppendLine();

        sb.AppendLine("## Reasoning");
        sb.AppendLine(reasoning);
        sb.AppendLine();

        sb.AppendLine("## Proposed Tables");
        sb.AppendLine();

        foreach (var table in tables)
        {
            sb.AppendLine($"### {table.TableName}");
            sb.AppendLine();
            sb.AppendLine($"**Source columns:** {string.Join(", ", table.SourceColumns)}");
            sb.AppendLine();
            sb.AppendLine("| Column | Type | Nullable | Primary Key | Foreign Key |");
            sb.AppendLine("|--------|------|----------|-------------|-------------|");
            foreach (var col in table.Columns)
            {
                var nullable = col.IsNullable ? "Yes" : "No";
                var pk       = col.IsPrimaryKey ? "Yes" : "No";
                var fk       = col.ForeignKeyTo ?? "—";
                sb.AppendLine($"| {col.Name} | {col.PostgresType} | {nullable} | {pk} | {fk} |");
            }
            sb.AppendLine();
        }

        var relationships = tables
            .SelectMany(t => t.Columns
                .Where(c => c.ForeignKeyTo is not null)
                .Select(c => $"- `{t.TableName}.{c.Name}` → `{c.ForeignKeyTo}`"))
            .ToList();

        if (relationships.Count > 0)
        {
            sb.AppendLine("## Relationships");
            sb.AppendLine();
            foreach (var rel in relationships)
                sb.AppendLine(rel);
            sb.AppendLine();
        }

        sb.AppendLine("## Source Column Mapping");
        sb.AppendLine();
        foreach (var table in tables)
        {
            var cols = table.SourceColumns.Count > 0
                ? string.Join(", ", table.SourceColumns)
                : "(none)";
            sb.AppendLine($"- `{table.TableName}`: [{cols}]");
        }

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatValue(string? rawValue, string postgresTypeStr)
        => SqlValueFormatter.Format(rawValue, ParsePostgresType(postgresTypeStr));

    private static PostgresType ParsePostgresType(string typeStr) =>
        typeStr.Trim().ToUpperInvariant() switch
        {
            "BOOLEAN" or "BOOL"                                       => PostgresType.Boolean,
            "INTEGER" or "INT"  or "INT4"                             => PostgresType.Integer,
            "BIGINT"  or "INT8"                                       => PostgresType.BigInt,
            "NUMERIC" or "DECIMAL"                                    => PostgresType.Numeric,
            "DATE"                                                    => PostgresType.Date,
            "TIMESTAMP" or "TIMESTAMPTZ" or "TIMESTAMP WITHOUT TIME ZONE"
                                                                      => PostgresType.Timestamp,
            _                                                         => PostgresType.Text,
        };

    private static string ParseFkTableName(string fkTarget)
    {
        var idx = fkTarget.IndexOf('(');
        return idx > 0 ? fkTarget[..idx] : fkTarget;
    }

    private static (string table, string column) ParseFkTarget(string fkTarget)
    {
        var idx = fkTarget.IndexOf('(');
        if (idx <= 0) return (fkTarget, string.Empty);
        return (fkTarget[..idx], fkTarget[(idx + 1)..].TrimEnd(')'));
    }
}
