using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxValueTypeMatcherTests
{
    public static TheoryData<SandboxValue, SandboxType> ValuesAndExpectedTypes()
    {
        var list = SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32);
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("key")] = SandboxValue.FromInt32(1)
            },
            SandboxType.String,
            SandboxType.I32);
        var record = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(1), SandboxValue.FromString("value")]);
        var nested = SandboxValue.FromRecord([list, record]);
        return new TheoryData<SandboxValue, SandboxType>
        {
            { SandboxValue.Unit, SandboxType.Unit },
            { SandboxValue.FromBool(true), SandboxType.Bool },
            { SandboxValue.FromInt32(1), SandboxType.I32 },
            { SandboxValue.FromInt32(1), SandboxType.String },
            { SandboxValue.FromInt64(1), SandboxType.I64 },
            { SandboxValue.FromDouble(1), SandboxType.F64 },
            { SandboxValue.FromString("value"), SandboxType.String },
            { SandboxValue.FromGuid(Guid.Empty), SandboxType.Guid },
            { SandboxValue.FromOpaqueId("MonsterId", "monster-1"), SandboxType.Scalar("MonsterId") },
            { SandboxValue.FromOpaqueId("MonsterId", "monster-1"), SandboxType.Scalar("PlayerId") },
            { SandboxValue.FromPath("config/settings.json"), SandboxType.SandboxPath },
            { SandboxValue.FromPath("config/settings.json"), SandboxType.SandboxUri },
            { SandboxValue.FromUri("https://example.test/resource"), SandboxType.SandboxUri },
            { SandboxValue.FromUri("https://example.test/resource"), SandboxType.SandboxPath },
            { list, SandboxType.List(SandboxType.I32) },
            { list, SandboxType.List(SandboxType.String) },
            { map, SandboxType.Map(SandboxType.String, SandboxType.I32) },
            { map, SandboxType.Map(SandboxType.String, SandboxType.String) },
            { record, SandboxType.Record([SandboxType.I32, SandboxType.String]) },
            { record, SandboxType.Record([SandboxType.I32, SandboxType.I32]) },
            { record, SandboxType.Record([SandboxType.I32]) },
            {
                nested,
                SandboxType.Record(
                [
                    SandboxType.List(SandboxType.I32),
                    SandboxType.Record([SandboxType.I32, SandboxType.String])
                ])
            },
            {
                nested,
                SandboxType.Record(
                [
                    SandboxType.List(SandboxType.I32),
                    SandboxType.Record([SandboxType.I32, SandboxType.I32])
                ])
            }
        };
    }

    [Theory]
    [MemberData(nameof(ValuesAndExpectedTypes))]
    public void Supported_value_exact_matcher_agrees_with_materialized_value_type(
        SandboxValue value,
        SandboxType expectedType)
        => Assert.Equal(
            value.Type.Equals(expectedType),
            SandboxValueTypeMatcher.MatchesExactType(value, expectedType));

    [Fact]
    public void Exact_matcher_rejects_an_unknown_subtype_that_claims_the_expected_type()
    {
        var value = new ClaimedI32Value();

        Assert.True(value.Type.Equals(SandboxType.I32));
        Assert.False(SandboxValueTypeMatcher.MatchesExactType(value, SandboxType.I32));
    }

    private sealed record ClaimedI32Value : SandboxValue
    {
        public override SandboxType Type => SandboxType.I32;
    }
}
