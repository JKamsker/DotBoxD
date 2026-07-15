using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledLiteralValidationTests
{
    [Fact]
    public void List_literal_rejects_a_mismatched_item_type()
    {
        var exception = Assert.Throws<SandboxRuntimeException>(() =>
            CompiledRuntime.ListLiteralValue(
                SandboxType.I32,
                [SandboxValue.FromString("wrong")]));

        Assert.Equal(SandboxErrorCode.InvalidInput, exception.Error.Code);
        Assert.Equal("list literal item type mismatch", exception.Error.SafeMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Map_literal_rejects_mismatched_entry_types(bool mismatchKey)
    {
        var keys = mismatchKey
            ? new[] { SandboxValue.FromInt32(1) }
            : [SandboxValue.FromString("key")];
        var values = mismatchKey
            ? new[] { SandboxValue.FromInt32(1) }
            : [SandboxValue.FromString("wrong")];

        var exception = Assert.Throws<SandboxRuntimeException>(() =>
            CompiledRuntime.MapLiteralValue(
                SandboxType.String,
                SandboxType.I32,
                keys,
                values));

        Assert.Equal(SandboxErrorCode.InvalidInput, exception.Error.Code);
        Assert.Equal("map literal entry type mismatch", exception.Error.SafeMessage);
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("opaque")]
    [InlineData("record")]
    public void Literal_validation_preserves_uncached_structural_fallbacks(string kind)
    {
        var (type, value) = StructuralValue(kind);

        var list = Assert.IsType<ListValue>(CompiledRuntime.ListLiteralValue(type, [value]));
        var map = Assert.IsType<MapValue>(CompiledRuntime.MapLiteralValue(
            SandboxType.String,
            type,
            [SandboxValue.FromString("key")],
            [value]));

        Assert.Same(type, list.ItemType);
        Assert.Same(type, map.ValueType);
    }

    [Fact]
    public void Literal_type_null_checks_keep_their_parameter_names()
    {
        Assert.Equal(
            "itemType",
            Assert.Throws<ArgumentNullException>(() =>
                CompiledRuntime.ListLiteralValue(null!, [])).ParamName);
        Assert.Equal(
            "keyType",
            Assert.Throws<ArgumentNullException>(() =>
                CompiledRuntime.MapLiteralValue(null!, SandboxType.I32, [], [])).ParamName);
        Assert.Equal(
            "valueType",
            Assert.Throws<ArgumentNullException>(() =>
                CompiledRuntime.MapLiteralValue(SandboxType.I32, null!, [], [])).ParamName);
    }

    [Fact]
    public void Map_literal_rejects_key_value_count_mismatch_before_type_validation()
    {
        var exception = Assert.Throws<SandboxRuntimeException>(() =>
            CompiledRuntime.MapLiteralValue(
                null!,
                SandboxType.I32,
                [SandboxValue.FromInt32(1)],
                []));

        Assert.Equal(SandboxErrorCode.InvalidInput, exception.Error.Code);
        Assert.Equal("map literal key/value count mismatch", exception.Error.SafeMessage);
    }

    [Fact]
    public void Charged_literals_keep_the_same_resource_accounting_as_explicit_charging()
    {
        var chargedListContext = CreateContext();
        var explicitListContext = CreateContext();
        _ = CompiledRuntime.ListLiteral(
            chargedListContext,
            SandboxType.I32,
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)]);
        explicitListContext.ChargeValue(CompiledRuntime.ListLiteralValue(
            SandboxType.I32,
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)]));

        var chargedMapContext = CreateContext();
        var explicitMapContext = CreateContext();
        _ = CompiledRuntime.MapLiteral(
            chargedMapContext,
            SandboxType.String,
            SandboxType.I32,
            [SandboxValue.FromString("key")],
            [SandboxValue.FromInt32(1)]);
        explicitMapContext.ChargeValue(CompiledRuntime.MapLiteralValue(
            SandboxType.String,
            SandboxType.I32,
            [SandboxValue.FromString("key")],
            [SandboxValue.FromInt32(1)]));

        var chargedListUsage = chargedListContext.Budget.Snapshot();
        var chargedMapUsage = chargedMapContext.Budget.Snapshot();

        Assert.Equal(explicitListContext.Budget.Snapshot(), chargedListUsage);
        Assert.Equal(0, chargedListUsage.FuelUsed);
        Assert.Equal(0, chargedListUsage.AllocatedBytes);
        Assert.Equal(2, chargedListUsage.CollectionElements);
        Assert.Equal(0, chargedListUsage.StringBytes);

        Assert.Equal(explicitMapContext.Budget.Snapshot(), chargedMapUsage);
        Assert.Equal(0, chargedMapUsage.FuelUsed);
        Assert.Equal(6, chargedMapUsage.AllocatedBytes);
        Assert.Equal(1, chargedMapUsage.CollectionElements);
        Assert.Equal(6, chargedMapUsage.StringBytes);
    }

    private static (SandboxType Type, SandboxValue Value) StructuralValue(string kind)
        => kind switch
        {
            "nested" => (
                SandboxType.List(SandboxType.I32),
                SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32)),
            "opaque" => (
                SandboxType.Scalar("PlayerId"),
                SandboxValue.FromOpaqueId("PlayerId", "player-1")),
            "record" => (
                SandboxType.Record([SandboxType.I32, SandboxType.String]),
                SandboxValue.FromRecord([SandboxValue.FromInt32(1), SandboxValue.FromString("field")])),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxWallTime: TimeSpan.FromMinutes(1),
            MaxAllocatedBytes: long.MaxValue,
            MaxListLength: int.MaxValue,
            MaxMapEntries: int.MaxValue,
            MaxTotalCollectionElements: long.MaxValue);
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }
}
