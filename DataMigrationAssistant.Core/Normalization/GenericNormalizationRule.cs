using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Normalization;

public sealed class GenericNormalizationRule : IDeterministicNormalizationRule
{
    private const string FallbackReasoning =
        "Generic deterministic fallback: no domain-specific rule matched, " +
        "so the sheet is preserved as a single table.";

    public bool CanHandle(NormalizationRequest request) => true;

    public NormalizationProposal Apply(NormalizationRequest request)
    {
        var schema = request.FlatSchema;

        // Prefer a candidate key named 'id'; fall back to the first candidate key found;
        // if none exist, a synthetic surrogate is prepended.
        var pkColumn =
            schema.Columns.FirstOrDefault(
                c => c.IsCandidateKey &&
                     string.Equals(c.SnakeCaseName, "id", StringComparison.OrdinalIgnoreCase))
            ?? schema.Columns.FirstOrDefault(c => c.IsCandidateKey);

        var columns       = new List<ProposedColumn>();
        var sourceColumns = new List<string>();

        if (pkColumn is null)
        {
            columns.Add(new ProposedColumn
            {
                Name         = "id",
                PostgresType = "INTEGER",
                IsNullable   = false,
                IsPrimaryKey = true,
            });
            // Synthetic id has no source column — omit from sourceColumns.
        }

        foreach (var col in schema.Columns)
        {
            var isPk = pkColumn is not null &&
                       string.Equals(col.SnakeCaseName, pkColumn.SnakeCaseName,
                                     StringComparison.OrdinalIgnoreCase);

            columns.Add(new ProposedColumn
            {
                Name         = col.SnakeCaseName,
                PostgresType = MapType(col.InferredType),
                IsNullable   = col.IsNullable,
                IsPrimaryKey = isPk,
                ForeignKeyTo = null,
            });

            sourceColumns.Add(col.SnakeCaseName);
        }

        return new NormalizationProposal
        {
            Reasoning = FallbackReasoning,
            Tables =
            [
                new ProposedTable
                {
                    TableName     = schema.TableName,
                    Columns       = columns,
                    SourceColumns = sourceColumns,
                },
            ],
        };
    }

    private static string MapType(PostgresType type) => type switch
    {
        PostgresType.Boolean   => "BOOLEAN",
        PostgresType.Integer   => "INTEGER",
        PostgresType.BigInt    => "BIGINT",
        PostgresType.Numeric   => "NUMERIC",
        PostgresType.Date      => "DATE",
        PostgresType.Timestamp => "TIMESTAMP",
        _                      => "TEXT",
    };
}
