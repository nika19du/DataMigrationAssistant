using DataMigrationAssistant.Core.Inference;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Utilities;

namespace DataMigrationAssistant.Core.Services;

public sealed class SchemaInferenceService : ISchemaInferenceService
{
    public TableSchema InferSchema(SheetPreview preview)
    {
        var columns = preview.Columns
            .Select(col => InferColumn(col, preview.Rows))
            .ToList();

        return new TableSchema
        {
            TableName = NamingUtility.ToSnakeCase(preview.SheetName),
            SheetName = preview.SheetName,
            Columns = columns,
            SampleRowCount = preview.Rows.Count,
        };
    }

    private static ColumnSchema InferColumn(
        ColumnInfo col,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        var values = rows
            .Select(r => r.TryGetValue(col.SnakeCaseName, out var v) ? v : null)
            .ToList();

        bool isNullable = values.Any(v => string.IsNullOrWhiteSpace(v));

        var nonNullValues = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        bool hasDuplicates = nonNullValues.Count != nonNullValues.Distinct(StringComparer.Ordinal).Count();
        bool isCandidateKey = !isNullable && !hasDuplicates && nonNullValues.Count > 0;

        var inferredType = TypeInferrer.InferColumnType(values);
        var quality      = ComputeCandidateKeyQuality(col.SnakeCaseName, inferredType, isNullable, hasDuplicates);

        return new ColumnSchema
        {
            Index              = col.Index,
            Name               = col.Name,
            SnakeCaseName      = col.SnakeCaseName,
            InferredType       = inferredType,
            IsNullable         = isNullable,
            HasDuplicates      = hasDuplicates,
            IsCandidateKey     = isCandidateKey,
            CandidateKeyQuality = quality,
        };
    }

    internal static CandidateKeyQuality ComputeCandidateKeyQuality(
        string name,
        PostgresType type,
        bool isNullable,
        bool hasDuplicates)
    {
        if (isNullable || hasDuplicates)
            return CandidateKeyQuality.None;

        // Types that cannot be meaningful database keys
        if (type is PostgresType.Boolean
                 or PostgresType.Numeric
                 or PostgresType.Date
                 or PostgresType.Timestamp)
            return CandidateKeyQuality.None;

        // Integer / BigInt / Text proceed to name scoring

        // Strong: surrogate or explicitly key-named patterns
        if (string.Equals(name, "id",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "code",   StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "key",    StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "number", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_id",     StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_code",   StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_key",    StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_number", StringComparison.OrdinalIgnoreCase))
            return CandidateKeyQuality.Strong;

        // Plausible: business identity patterns (typically text)
        if (string.Equals(name, "username", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "email",    StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "name",     StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_name",     StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_email",    StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_username", StringComparison.OrdinalIgnoreCase))
            return CandidateKeyQuality.Plausible;

        // Type fits but name does not suggest a key
        return CandidateKeyQuality.Weak;
    }
}
