using DataMigrationAssistant.Core.Inference;
using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Tests;

public sealed class TypeInferrerTests
{
    // ── ClassifyValue ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("true",  PostgresType.Boolean)]
    [InlineData("false", PostgresType.Boolean)]
    [InlineData("yes",   PostgresType.Boolean)]
    [InlineData("no",    PostgresType.Boolean)]
    [InlineData("y",     PostgresType.Boolean)]
    [InlineData("n",     PostgresType.Boolean)]
    [InlineData("t",     PostgresType.Boolean)]
    [InlineData("f",     PostgresType.Boolean)]
    [InlineData("TRUE",  PostgresType.Boolean)]
    [InlineData("False", PostgresType.Boolean)]
    public void ClassifyValue_BooleanLiterals_ReturnsBoolean(string value, PostgresType expected)
        => Assert.Equal(expected, TypeInferrer.ClassifyValue(value));

    [Theory]
    [InlineData("1",           PostgresType.Integer)]
    [InlineData("0",           PostgresType.Integer)]
    [InlineData("42",          PostgresType.Integer)]
    [InlineData("-7",          PostgresType.Integer)]
    [InlineData("2147483647",  PostgresType.Integer)]  // int.MaxValue
    [InlineData("-2147483648", PostgresType.Integer)]  // int.MinValue
    public void ClassifyValue_Int32Range_ReturnsInteger(string value, PostgresType expected)
        => Assert.Equal(expected, TypeInferrer.ClassifyValue(value));

    [Theory]
    [InlineData("2147483648",   PostgresType.BigInt)]  // int.MaxValue + 1
    [InlineData("-2147483649",  PostgresType.BigInt)]  // int.MinValue - 1
    [InlineData("9999999999",   PostgresType.BigInt)]
    public void ClassifyValue_Int64BeyondInt32_ReturnsBigInt(string value, PostgresType expected)
        => Assert.Equal(expected, TypeInferrer.ClassifyValue(value));

    [Theory]
    [InlineData("3.14",       PostgresType.Numeric)]
    [InlineData("-1.5",       PostgresType.Numeric)]
    [InlineData("0.001",      PostgresType.Numeric)]
    [InlineData("1234567.89", PostgresType.Numeric)]
    public void ClassifyValue_DecimalValues_ReturnsNumeric(string value, PostgresType expected)
        => Assert.Equal(expected, TypeInferrer.ClassifyValue(value));

    [Theory]
    [InlineData("2023-01-15", PostgresType.Date)]
    [InlineData("2000-12-31", PostgresType.Date)]
    public void ClassifyValue_DateOnlyStrings_ReturnsDate(string value, PostgresType expected)
        => Assert.Equal(expected, TypeInferrer.ClassifyValue(value));

    [Theory]
    [InlineData("2023-01-15 10:30:00", PostgresType.Timestamp)]
    [InlineData("2023-01-15T10:30:00", PostgresType.Timestamp)]
    public void ClassifyValue_DateTimeStrings_ReturnsTimestamp(string value, PostgresType expected)
        => Assert.Equal(expected, TypeInferrer.ClassifyValue(value));

    [Theory]
    [InlineData("hello world")]
    [InlineData("abc123")]
    [InlineData("N/A")]
    [InlineData("not-a-date")]
    public void ClassifyValue_ArbitraryStrings_ReturnsText(string value)
        => Assert.Equal(PostgresType.Text, TypeInferrer.ClassifyValue(value));

    // ── Promote ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PostgresType.Boolean,   PostgresType.Boolean,   PostgresType.Boolean)]
    [InlineData(PostgresType.Boolean,   PostgresType.Integer,   PostgresType.Integer)]
    [InlineData(PostgresType.Boolean,   PostgresType.BigInt,    PostgresType.BigInt)]
    [InlineData(PostgresType.Boolean,   PostgresType.Numeric,   PostgresType.Numeric)]
    [InlineData(PostgresType.Integer,   PostgresType.BigInt,    PostgresType.BigInt)]
    [InlineData(PostgresType.Integer,   PostgresType.Numeric,   PostgresType.Numeric)]
    [InlineData(PostgresType.BigInt,    PostgresType.Numeric,   PostgresType.Numeric)]
    [InlineData(PostgresType.Date,      PostgresType.Timestamp, PostgresType.Timestamp)]
    public void Promote_SameDomain_ReturnsLessSpecificType(PostgresType a, PostgresType b, PostgresType expected)
        => Assert.Equal(expected, TypeInferrer.Promote(a, b));

    [Theory]
    [InlineData(PostgresType.Integer,   PostgresType.Date)]
    [InlineData(PostgresType.Numeric,   PostgresType.Date)]
    [InlineData(PostgresType.Boolean,   PostgresType.Timestamp)]
    [InlineData(PostgresType.BigInt,    PostgresType.Timestamp)]
    [InlineData(PostgresType.Integer,   PostgresType.Timestamp)]
    public void Promote_IncompatibleDomains_ReturnsText(PostgresType a, PostgresType b)
        => Assert.Equal(PostgresType.Text, TypeInferrer.Promote(a, b));

    [Theory]
    [InlineData(PostgresType.Integer, PostgresType.Text)]
    [InlineData(PostgresType.Date,    PostgresType.Text)]
    [InlineData(PostgresType.Boolean, PostgresType.Text)]
    public void Promote_AnyWithText_ReturnsText(PostgresType a, PostgresType b)
        => Assert.Equal(PostgresType.Text, TypeInferrer.Promote(a, b));

    // ── InferColumnType ────────────────────────────────────────────────────────

    [Fact]
    public void InferColumnType_AllIntegers_ReturnsInteger()
        => Assert.Equal(PostgresType.Integer, TypeInferrer.InferColumnType(["1", "2", "3"]));

    [Fact]
    public void InferColumnType_MixedIntAndBigInt_ReturnsBigInt()
        => Assert.Equal(PostgresType.BigInt, TypeInferrer.InferColumnType(["42", "9999999999"]));

    [Fact]
    public void InferColumnType_MixedBoolAndNumeric_ReturnsNumeric()
        => Assert.Equal(PostgresType.Numeric, TypeInferrer.InferColumnType(["true", "1", "3.14"]));

    [Fact]
    public void InferColumnType_DateAndTimestamp_ReturnsTimestamp()
        => Assert.Equal(PostgresType.Timestamp, TypeInferrer.InferColumnType(["2023-01-15", "2023-01-16 08:00:00"]));

    [Fact]
    public void InferColumnType_NumericAndDate_ReturnsText()
        => Assert.Equal(PostgresType.Text, TypeInferrer.InferColumnType(["42", "2023-01-15"]));

    [Fact]
    public void InferColumnType_NullsAreIgnored_TypeInferredFromNonNulls()
        => Assert.Equal(PostgresType.Integer, TypeInferrer.InferColumnType([null, "1", null, "2"]));

    [Fact]
    public void InferColumnType_AllNulls_ReturnsText()
        => Assert.Equal(PostgresType.Text, TypeInferrer.InferColumnType([null, null]));

    [Fact]
    public void InferColumnType_EmptyCollection_ReturnsText()
        => Assert.Equal(PostgresType.Text, TypeInferrer.InferColumnType([]));

    [Fact]
    public void InferColumnType_SingleBoolValue_ReturnsBoolean()
        => Assert.Equal(PostgresType.Boolean, TypeInferrer.InferColumnType(["true"]));
}
