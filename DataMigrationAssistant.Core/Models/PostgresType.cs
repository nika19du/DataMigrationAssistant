namespace DataMigrationAssistant.Core.Models;

/// <summary>
/// Inferred PostgreSQL column type. Integer values encode specificity within each domain —
/// promotion picks the higher value when two types from the same domain are combined.
/// Numeric domain: 0–3. Temporal domain: 10–11. Text: 99 (universal fallback).
/// </summary>
public enum PostgresType
{
    Boolean   = 0,
    Integer   = 1,
    BigInt    = 2,
    Numeric   = 3,
    Date      = 10,
    Timestamp = 11,
    Text      = 99,
}
