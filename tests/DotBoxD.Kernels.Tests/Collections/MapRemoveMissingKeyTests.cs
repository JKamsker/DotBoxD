using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Collections;

public sealed class MapRemoveMissingKeyTests
{
    [Theory]
    [InlineData(RemoveMode.Compiled)]
    [InlineData(RemoveMode.Interpreted)]
    public void Missing_key_reuses_dictionary_backed_source_and_preserves_accounting(RemoveMode mode)
    {
        var source = CreateSource();
        var context = CreateContext();

        var result = Remove(mode, context, source, SandboxValue.FromInt32(99));

        Assert.Same(source, result);
        Assert.Equal(ExpectedUsage(source), context.Budget.Snapshot());
    }

    [Theory]
    [InlineData(RemoveMode.Compiled)]
    [InlineData(RemoveMode.Interpreted)]
    public void Empty_map_missing_key_reuses_source_and_preserves_accounting(RemoveMode mode)
    {
        var source = (MapValue)SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>(),
            SandboxType.I32,
            SandboxType.I32);
        var context = CreateContext();

        var result = Remove(mode, context, source, SandboxValue.FromInt32(99));

        Assert.Same(source, result);
        Assert.Equal(ExpectedUsage(source), context.Budget.Snapshot());
    }

    [Theory]
    [InlineData(RemoveMode.Compiled)]
    [InlineData(RemoveMode.Interpreted)]
    public void String_map_missing_key_reuses_source_and_preserves_accounting(RemoveMode mode)
    {
        var source = CreateStringSource();
        var context = CreateContext();

        var result = Remove(mode, context, source, SandboxValue.FromString("missing"));

        Assert.Same(source, result);
        Assert.Equal(ExpectedUsage(source), context.Budget.Snapshot());
    }

    [Theory]
    [InlineData(RemoveMode.Compiled)]
    [InlineData(RemoveMode.Interpreted)]
    public void Present_key_returns_distinct_map_without_mutating_source(RemoveMode mode)
    {
        var source = CreateSource();

        var result = Assert.IsType<MapValue>(
            Remove(mode, CreateContext(), source, SandboxValue.FromInt32(1)));

        Assert.NotSame(source, result);
        Assert.Equal(2, source.Values.Count);
        Assert.True(source.Values.ContainsKey(SandboxValue.FromInt32(1)));
        Assert.Single(result.Values);
        Assert.False(result.Values.ContainsKey(SandboxValue.FromInt32(1)));
    }

    [Theory]
    [InlineData(RemoveMode.Compiled)]
    [InlineData(RemoveMode.Interpreted)]
    public void Wrong_key_type_fails_before_resource_accounting(RemoveMode mode)
    {
        var context = CreateContext();

        var error = Assert.Throws<SandboxRuntimeException>(
            () => Remove(mode, context, CreateSource(), SandboxValue.FromString("wrong")));

        Assert.Equal(SandboxErrorCode.InvalidInput, error.Error.Code);
        Assert.Equal(CreateContext().Budget.Snapshot(), context.Budget.Snapshot());
    }

    [Theory]
    [InlineData(RemoveMode.Compiled)]
    [InlineData(RemoveMode.Interpreted)]
    public void Missing_key_fuel_quota_fails_before_copy_allocation(RemoveMode mode)
    {
        var context = CreateContext(maxFuel: 3);

        var error = Assert.Throws<SandboxRuntimeException>(
            () => Remove(mode, context, CreateSource(), SandboxValue.FromInt32(99)));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, error.Error.Code);
        var usage = context.Budget.Snapshot();
        Assert.Equal(4, usage.FuelUsed);
        Assert.Equal(0, usage.AllocatedBytes);
        Assert.Equal(0, usage.CollectionElements);
        Assert.Equal(0, usage.StringBytes);
    }

    [Theory]
    [InlineData(RemoveMode.Compiled)]
    [InlineData(RemoveMode.Interpreted)]
    public void Missing_key_charges_copy_allocation_before_return(RemoveMode mode)
    {
        var source = CreateSource();
        var context = CreateContext(maxAllocatedBytes: 16);

        var error = Assert.Throws<SandboxRuntimeException>(
            () => Remove(mode, context, source, SandboxValue.FromInt32(99)));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, error.Error.Code);
        var usage = context.Budget.Snapshot();
        Assert.Equal(4, usage.FuelUsed);
        Assert.Equal(64, usage.AllocatedBytes);
        Assert.Equal(0, usage.CollectionElements);
        Assert.Equal(0, usage.StringBytes);
    }

    [Theory]
    [InlineData(RemoveMode.Compiled)]
    [InlineData(RemoveMode.Interpreted)]
    public void Missing_key_string_quota_fails_after_projected_allocation(RemoveMode mode)
    {
        var context = CreateContext(maxStringBytes: 0);

        var error = Assert.Throws<SandboxRuntimeException>(
            () => Remove(mode, context, CreateStringSource(), SandboxValue.FromString("missing")));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, error.Error.Code);
        var usage = context.Budget.Snapshot();
        Assert.Equal(3, usage.FuelUsed);
        Assert.Equal(44, usage.AllocatedBytes);
        Assert.Equal(1, usage.CollectionElements);
        Assert.Equal(12, usage.StringBytes);
    }

    private static SandboxValue Remove(
        RemoveMode mode,
        SandboxContext context,
        MapValue source,
        SandboxValue key)
        => mode switch
        {
            RemoveMode.Compiled => CompiledRuntime.MapRemove(context, source, key),
            RemoveMode.Interpreted => CollectionOperations.RemoveMapValue(key, source, context),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private static SandboxResourceUsage ExpectedUsage(MapValue source)
    {
        var context = CreateContext();
        context.ChargeFuel(SandboxCollectionFuel.Copy(source.Values.Count));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(
            source.Values.Count,
            bytesPerElement: 32,
            minimumOne: true));
        context.ChargeValue(source);
        return context.Budget.Snapshot();
    }

    private static MapValue CreateSource()
        => (MapValue)SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromInt32(1)] = SandboxValue.FromInt32(10),
                [SandboxValue.FromInt32(2)] = SandboxValue.FromInt32(20)
            },
            SandboxType.I32,
            SandboxType.I32);

    private static MapValue CreateStringSource()
        => (MapValue)SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("a")] = SandboxValue.FromString("alpha")
            },
            SandboxType.String,
            SandboxType.String);

    private static SandboxContext CreateContext(
        long maxAllocatedBytes = long.MaxValue,
        long maxFuel = long.MaxValue,
        long maxStringBytes = long.MaxValue)
    {
        var limits = new ResourceLimits(
            MaxFuel: maxFuel,
            MaxWallTime: TimeSpan.FromMinutes(5),
            MaxAllocatedBytes: maxAllocatedBytes,
            MaxMapEntries: int.MaxValue,
            MaxTotalCollectionElements: long.MaxValue,
            MaxTotalStringBytes: maxStringBytes);
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    public enum RemoveMode
    {
        Compiled,
        Interpreted
    }
}
