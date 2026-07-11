using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxRecordContractTests
{
    [Fact]
    public void Empty_record_factories_fail_closed_at_public_boundary()
    {
        Assert.Throws<ArgumentException>(() =>
            SandboxValue.FromRecord(Array.Empty<SandboxValue>()));

        Assert.Throws<ArgumentException>(() =>
            SandboxType.Record(Array.Empty<SandboxType>()));
    }

    [Fact]
    public void Empty_record_types_fail_closed_for_all_public_construction_paths()
    {
        var nonEmptyRecord = SandboxType.Record([SandboxType.I32]);

        Assert.Throws<ArgumentException>(() =>
            new SandboxType(SandboxType.RecordName, []));

        Assert.Throws<ArgumentException>(() =>
            nonEmptyRecord with { Arguments = [] });

        Assert.Throws<ArgumentException>(() =>
            SandboxType.Scalar("PlayerId") with { Name = SandboxType.RecordName });
    }

    [Fact]
    public void Owned_record_factory_rejects_null_array_before_wrapping()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            SandboxValue.FromOwnedRecord(null!));

        Assert.Equal("fields", ex.ParamName);
    }

    [Fact]
    public void Non_empty_records_report_known_self_type_and_validate()
    {
        var value = SandboxValue.FromRecord([
            SandboxValue.FromInt32(42),
            SandboxValue.FromString("ready")
        ]);

        var type = value.Type;

        Assert.True(type.IsRecord);
        Assert.True(type.IsKnown());
        Assert.Equal(SandboxType.Record([SandboxType.I32, SandboxType.String]), type);
        SandboxValueValidator.RequireType(value, type, "bad input");
    }
}
