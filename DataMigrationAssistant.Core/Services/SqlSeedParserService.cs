using System.Text;
using System.Text.RegularExpressions;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public sealed class SqlSeedParserService : ISqlSeedParserService
{
    private static readonly Regex InsertPattern = new(
        @"^INSERT\s+INTO\s+(\w+)\s*\(([^)]+)\)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ServiceResult<SeedRecord> Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return ServiceResult<SeedRecord>.Fail("SQL input is empty.");

        var lines = sql
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("--"))
            .ToArray();

        var insertIdx = Array.FindIndex(lines,
            l => l.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase));

        if (insertIdx < 0)
            return ServiceResult<SeedRecord>.Fail("No INSERT INTO statement found.");

        var insertMatch = InsertPattern.Match(lines[insertIdx]);
        if (!insertMatch.Success)
            return ServiceResult<SeedRecord>.Fail($"Could not parse INSERT INTO line: {lines[insertIdx]}");

        var tableName = insertMatch.Groups[1].Value;
        var columns   = insertMatch.Groups[2].Value
            .Split(',')
            .Select(c => c.Trim())
            .ToList();

        var valuesIdx = Array.FindIndex(lines, insertIdx + 1,
            l => l.Equals("VALUES", StringComparison.OrdinalIgnoreCase));

        if (valuesIdx < 0)
            return ServiceResult<SeedRecord>.Fail("No VALUES clause found.");

        // Collect row lines — each generated row starts with '('
        var rowLines = lines
            .Skip(valuesIdx + 1)
            .TakeWhile(l => l.StartsWith('('))
            .ToList();

        if (rowLines.Count == 0)
            return ServiceResult<SeedRecord>.Fail("No value rows found.");

        var rows = new List<IReadOnlyList<string?>>(rowLines.Count);
        foreach (var line in rowLines)
        {
            // Strip trailing comma or semicolon left by the last-row / ON CONFLICT boundary
            var cleaned = line.TrimEnd(',', ';');
            if (!cleaned.StartsWith('(') || !cleaned.EndsWith(')'))
                return ServiceResult<SeedRecord>.Fail($"Invalid row format: {line}");

            rows.Add(TokenizeValues(cleaned[1..^1]));
        }

        return ServiceResult<SeedRecord>.Ok(new SeedRecord
        {
            TableName = tableName,
            Columns   = columns,
            Rows      = rows,
        });
    }

    // Tokenizes the inner content of a row, e.g. "1, 'Alice''s cat', NULL, TRUE, 3.14"
    // Quoted strings have '' unescaped to '; unquoted NULL becomes null; everything else kept as-is.
    private static IReadOnlyList<string?> TokenizeValues(string inner)
    {
        var values = new List<string?>();
        int i = 0;

        while (i < inner.Length)
        {
            while (i < inner.Length && inner[i] == ' ') i++;
            if (i >= inner.Length) break;

            if (inner[i] == '\'')
            {
                i++; // skip opening quote
                var sb = new StringBuilder();
                while (i < inner.Length)
                {
                    if (inner[i] == '\'')
                    {
                        if (i + 1 < inner.Length && inner[i + 1] == '\'')
                        {
                            sb.Append('\'');
                            i += 2;
                        }
                        else
                        {
                            i++; // skip closing quote
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(inner[i++]);
                    }
                }
                values.Add(sb.ToString());
            }
            else
            {
                // Unquoted token: read to next comma
                int start = i;
                while (i < inner.Length && inner[i] != ',') i++;
                var token = inner[start..i].Trim();
                values.Add(string.Equals(token, "NULL", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : token);
            }

            // Skip separator
            while (i < inner.Length && inner[i] == ' ') i++;
            if (i < inner.Length && inner[i] == ',') i++;
        }

        return values;
    }
}
