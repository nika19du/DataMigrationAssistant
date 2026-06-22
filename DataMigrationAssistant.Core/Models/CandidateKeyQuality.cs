namespace DataMigrationAssistant.Core.Models;

/// <summary>
/// Semantic quality of a candidate key column, combining type fitness and naming convention.
/// A column may be structurally unique (IsCandidateKey = true) but semantically unsuitable
/// as a database key — this enum captures that distinction.
/// </summary>
public enum CandidateKeyQuality
{
    /// <summary>Not a candidate key: nullable, has duplicates, or type disqualifies it (Boolean/Numeric/Date/Timestamp).</summary>
    None = 0,

    /// <summary>Unique by coincidence in the sample; type fits (Integer/BigInt/Text) but name is not key-idiomatic.</summary>
    Weak = 1,

    /// <summary>Text column with a business-identity name (username, email, *_name).</summary>
    Plausible = 2,

    /// <summary>Integer or Text column with an explicit key name (id, *_id, *_code, *_key, *_number).</summary>
    Strong = 3,
}
