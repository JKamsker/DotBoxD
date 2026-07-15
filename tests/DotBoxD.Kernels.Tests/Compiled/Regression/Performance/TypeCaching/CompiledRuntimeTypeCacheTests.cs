using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class CompiledRuntimeTypeCacheTests
{
    [Fact]
    public void All_builtin_list_and_map_types_are_reused_without_index_collisions()
    {
        var builtinTypes = BuiltinScalarTypes();
        foreach (var itemType in builtinTypes)
        {
            var listType = CompiledRuntime.TypeListCached(itemType);

            Assert.Same(listType, CompiledRuntime.TypeListCached(itemType));
            Assert.Same(itemType, listType.Arguments[0]);
        }

        foreach (var keyType in builtinTypes)
        {
            foreach (var valueType in builtinTypes)
            {
                var mapType = CompiledRuntime.TypeMapCached(keyType, valueType);

                Assert.Same(mapType, CompiledRuntime.TypeMapCached(keyType, valueType));
                Assert.Same(keyType, mapType.Arguments[0]);
                Assert.Same(valueType, mapType.Arguments[1]);
            }
        }
    }

    [Fact]
    public void Unsupported_cached_factory_operands_stay_on_the_uncached_path()
    {
        var unsupportedOperands = new[]
        {
            SandboxType.Scalar("MonsterId"),
            SandboxType.List(SandboxType.I32),
            SandboxType.Record([SandboxType.I32]),
            SandboxType.Scalar("I32")
        };

        foreach (var operand in unsupportedOperands)
        {
            AssertUncachedList(operand);
            AssertUncachedMap(SandboxType.String, operand);
            AssertUncachedMap(operand, SandboxType.I32);
        }
    }

    [Fact]
    public void Null_operands_keep_sandbox_type_factory_errors()
    {
        Assert.Equal("item", Assert.Throws<ArgumentNullException>(() => CompiledRuntime.TypeList(null!)).ParamName);
        Assert.Equal("item", Assert.Throws<ArgumentNullException>(() => CompiledRuntime.TypeListCached(null!)).ParamName);
        Assert.Equal(
            "key",
            Assert.Throws<ArgumentNullException>(() => CompiledRuntime.TypeMap(null!, SandboxType.I32)).ParamName);
        Assert.Equal(
            "value",
            Assert.Throws<ArgumentNullException>(() => CompiledRuntime.TypeMap(SandboxType.I32, null!)).ParamName);
        Assert.Equal(
            "key",
            Assert.Throws<ArgumentNullException>(() => CompiledRuntime.TypeMapCached(null!, SandboxType.I32)).ParamName);
        Assert.Equal(
            "value",
            Assert.Throws<ArgumentNullException>(() => CompiledRuntime.TypeMapCached(SandboxType.I32, null!)).ParamName);
    }

    [Fact]
    public void Concurrent_calls_reuse_one_builtin_structural_type()
    {
        var lists = new SandboxType[32];
        var maps = new SandboxType[32];

        Parallel.For(0, lists.Length, i =>
        {
            lists[i] = CompiledRuntime.TypeListCached(SandboxType.Guid);
            maps[i] = CompiledRuntime.TypeMapCached(SandboxType.Guid, SandboxType.SandboxUri);
        });

        Assert.All(lists, type => Assert.Same(lists[0], type));
        Assert.All(maps, type => Assert.Same(maps[0], type));
    }

    [Fact]
    [Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
    public void Warm_builtin_structural_type_cache_hits_do_not_allocate()
    {
        const int iterations = 100_000;
        var listType = CompiledRuntime.TypeListCached(SandboxType.I32);
        var mapType = CompiledRuntime.TypeMapCached(SandboxType.String, SandboxType.I32);

        for (var i = 0; i < 20_000; i++)
        {
            EnsureSame(listType, CompiledRuntime.TypeListCached(SandboxType.I32));
            EnsureSame(mapType, CompiledRuntime.TypeMapCached(SandboxType.String, SandboxType.I32));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            EnsureSame(listType, CompiledRuntime.TypeListCached(SandboxType.I32));
            EnsureSame(mapType, CompiledRuntime.TypeMapCached(SandboxType.String, SandboxType.I32));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(0, allocated);
        GC.KeepAlive(listType);
        GC.KeepAlive(mapType);
    }

    private static void EnsureSame(SandboxType expected, SandboxType actual)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException("Expected a built-in structural type cache hit.");
        }
    }

    private static SandboxType[] BuiltinScalarTypes()
        =>
        [
            SandboxType.Unit,
            SandboxType.Bool,
            SandboxType.I32,
            SandboxType.I64,
            SandboxType.F64,
            SandboxType.String,
            SandboxType.Guid,
            SandboxType.SandboxPath,
            SandboxType.SandboxUri
        ];

    private static void AssertUncachedList(SandboxType itemType)
    {
        var first = CompiledRuntime.TypeListCached(itemType);
        var second = CompiledRuntime.TypeListCached(itemType);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    private static void AssertUncachedMap(SandboxType keyType, SandboxType valueType)
    {
        var first = CompiledRuntime.TypeMapCached(keyType, valueType);
        var second = CompiledRuntime.TypeMapCached(keyType, valueType);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }
}
