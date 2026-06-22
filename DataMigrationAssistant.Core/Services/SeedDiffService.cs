using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;
using DataMigrationAssistant.Core.Utilities;

namespace DataMigrationAssistant.Core.Services;

public sealed class SeedDiffService : ISeedDiffService
{
    public ServiceResult<SeedDiffResult> Diff(
        SeedRecord oldSeed,
        SheetPreview newData,
        TableSchema schema)
    {
        var keyCol = schema.Columns.FirstOrDefault(c => c.IsCandidateKey);
        if (keyCol is null)
            return ServiceResult<SeedDiffResult>.Fail(
                $"Cannot diff '{schema.TableName}': no candidate key detected.");

        var keyName = keyCol.SnakeCaseName;

        if (!oldSeed.Columns.Contains(keyName, StringComparer.OrdinalIgnoreCase))
            return ServiceResult<SeedDiffResult>.Fail(
                $"Key column '{keyName}' not found in old seed columns.");

        if (!newData.Columns.Any(c => c.SnakeCaseName == keyName))
            return ServiceResult<SeedDiffResult>.Fail(
                $"Key column '{keyName}' not found in new Excel data.");

        var compareColumns = schema.Columns.Select(c => c.SnakeCaseName).ToList();

        // Index of the key in the old seed's positional column list
        var keyIndex = oldSeed.Columns
            .Select((name, i) => (name, i))
            .First(t => string.Equals(t.name, keyName, StringComparison.OrdinalIgnoreCase))
            .i;

        // Build normalized map for old seed rows: keyValue → {colName → normalizedValue}
        var oldMap = new Dictionary<string, Dictionary<string, string?>>();
        foreach (var row in oldSeed.Rows)
        {
            var rawKey = keyIndex < row.Count ? row[keyIndex] : null;
            var key    = Normalize(rawKey);
            if (key is null) continue;
            oldMap[key] = NormalizeOldRow(row, oldSeed.Columns);
        }

        // Build normalized map for new Excel rows: keyValue → {colName → normalizedValue}
        var newMap = new Dictionary<string, Dictionary<string, string?>>();
        foreach (var row in newData.Rows)
        {
            row.TryGetValue(keyName, out var rawKey);
            var key = Normalize(rawKey);
            if (key is null) continue;
            newMap[key] = NormalizeNewRow(row);
        }

        var diffRows = new List<SeedDiffRow>();

        // New-data rows first: Added, Changed, Unchanged (preserves Excel row order)
        foreach (var (key, newRow) in newMap)
        {
            if (!oldMap.TryGetValue(key, out var oldRow))
            {
                diffRows.Add(new SeedDiffRow
                {
                    Status       = SeedDiffStatus.Added,
                    KeyValue     = key,
                    NewRowValues = newRow,
                });
            }
            else
            {
                var changes = ComputeChanges(oldRow, newRow, compareColumns);
                diffRows.Add(new SeedDiffRow
                {
                    Status   = changes.Count > 0 ? SeedDiffStatus.Changed : SeedDiffStatus.Unchanged,
                    KeyValue = key,
                    Changes  = changes,
                });
            }
        }

        // Old-only rows: Removed (preserves SQL row order)
        foreach (var key in oldMap.Keys)
        {
            if (!newMap.ContainsKey(key))
                diffRows.Add(new SeedDiffRow { Status = SeedDiffStatus.Removed, KeyValue = key });
        }

        return ServiceResult<SeedDiffResult>.Ok(new SeedDiffResult
        {
            TableName     = schema.TableName,
            KeyColumnName = keyName,
            Rows          = diffRows,
        });
    }

    // Normalize: null/whitespace → null; TRUE/FALSE case-fold; decimals to InvariantCulture; otherwise trim.
    // Decimal normalisation makes "9,5" and "9.5" compare as equal across cultures.
    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        if (string.Equals(t, "TRUE",  StringComparison.OrdinalIgnoreCase)) return "TRUE";
        if (string.Equals(t, "FALSE", StringComparison.OrdinalIgnoreCase)) return "FALSE";
        return NumericParser.ToInvariantString(t) ?? t;
    }

    private static Dictionary<string, string?> NormalizeOldRow(
        IReadOnlyList<string?> row,
        IReadOnlyList<string> columns)
    {
        var dict = new Dictionary<string, string?>();
        for (int i = 0; i < columns.Count && i < row.Count; i++)
            dict[columns[i]] = Normalize(row[i]);
        return dict;
    }

    private static Dictionary<string, string?> NormalizeNewRow(
        IReadOnlyDictionary<string, string?> row)
        => row.ToDictionary(kvp => kvp.Key, kvp => Normalize(kvp.Value));

    private static List<SeedDiffCellChange> ComputeChanges(
        Dictionary<string, string?> oldRow,
        Dictionary<string, string?> newRow,
        IReadOnlyList<string> compareColumns)
    {
        var changes = new List<SeedDiffCellChange>();
        foreach (var col in compareColumns)
        {
            oldRow.TryGetValue(col, out var oldVal);
            newRow.TryGetValue(col, out var newVal);

            if (!string.Equals(oldVal, newVal, StringComparison.Ordinal))
                changes.Add(new SeedDiffCellChange
                {
                    ColumnName = col,
                    OldValue   = oldVal,
                    NewValue   = newVal,
                });
        }
        return changes;
    }
}
