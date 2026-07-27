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
    public void All_builtin_one_and_two_field_record_types_are_reused_without_index_collisions()
    {
        var builtinTypes = BuiltinScalarTypes();
        var cachedRecords = new HashSet<SandboxType>(ReferenceEqualityComparer.Instance);
        foreach (var fieldType in builtinTypes)
        {
            var recordType = CompiledRuntime.TypeRecordCached([fieldType]);

            Assert.Same(recordType, CompiledRuntime.TypeRecordCached([fieldType]));
            Assert.Same(fieldType, recordType.Arguments[0]);
            Assert.True(cachedRecords.Add(recordType));
        }

        foreach (var firstFieldType in builtinTypes)
        {
            foreach (var secondFieldType in builtinTypes)
            {
                var recordType = CompiledRuntime.TypeRecordCached([firstFieldType, secondFieldType]);

                Assert.Same(recordType, CompiledRuntime.TypeRecordCached([firstFieldType, secondFieldType]));
                Assert.Same(firstFieldType, recordType.Arguments[0]);
                Assert.Same(secondFieldType, recordType.Arguments[1]);
                Assert.True(cachedRecords.Add(recordType));
            }
        }

        Assert.Equal(90, cachedRecords.Count);
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
            AssertUncachedRecord([operand]);
            AssertUncachedRecord([SandboxType.I32, operand]);
            AssertUncachedRecord([operand, SandboxType.I32]);
        }

        AssertUncachedRecord([SandboxType.I32, SandboxType.String, SandboxType.Bool]);
    }

    [Fact]
    public void Legacy_record_factory_remains_uncached_for_cacheable_shapes()
    {
        AssertLegacyRecordUncached([SandboxType.I32]);
        AssertLegacyRecordUncached([SandboxType.I32, SandboxType.String]);
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
        AssertSameRecordFailure(null!);
        AssertSameRecordFailure([]);
        AssertSameRecordFailure([null!]);
        AssertSameRecordFailure([SandboxType.I32, null!]);
    }

    [Fact]
    public void Concurrent_calls_reuse_one_builtin_structural_type()
    {
        var lists = new SandboxType[32];
        var maps = new SandboxType[32];
        var records = new SandboxType[32];

        Parallel.For(0, lists.Length, i =>
        {
            lists[i] = CompiledRuntime.TypeListCached(SandboxType.Guid);
            maps[i] = CompiledRuntime.TypeMapCached(SandboxType.Guid, SandboxType.SandboxUri);
            records[i] = CompiledRuntime.TypeRecordCached([SandboxType.Guid, SandboxType.SandboxUri]);
        });

        Assert.All(lists, type => Assert.Same(lists[0], type));
        Assert.All(maps, type => Assert.Same(maps[0], type));
        Assert.All(records, type => Assert.Same(records[0], type));
    }

    [Fact]
    public void Cached_record_does_not_retain_or_observe_the_callers_mutable_array()
    {
        var fields = new[] { SandboxType.I32, SandboxType.String };

        var original = CompiledRuntime.TypeRecordCached(fields);
        fields[0] = SandboxType.Bool;
        var mutated = CompiledRuntime.TypeRecordCached(fields);

        Assert.Same(SandboxType.I32, original.Arguments[0]);
        Assert.Same(SandboxType.String, original.Arguments[1]);
        Assert.Same(SandboxType.Bool, mutated.Arguments[0]);
        Assert.Same(SandboxType.String, mutated.Arguments[1]);
        Assert.NotSame(original, mutated);
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

    [Fact]
    [Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
    public void Warm_builtin_record_cache_hits_allocate_only_the_callers_field_arrays()
    {
        const int iterations = 100_000;
        var oneField = CompiledRuntime.TypeRecordCached([SandboxType.I32]);
        var twoFields = CompiledRuntime.TypeRecordCached([SandboxType.I32, SandboxType.String]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            EnsureSame(oneField, CompiledRuntime.TypeRecordCached([SandboxType.I32]));
            EnsureSame(twoFields, CompiledRuntime.TypeRecordCached([SandboxType.I32, SandboxType.String]));
        }

        Assert.Equal(7_200_000, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        GC.KeepAlive(oneField);
        GC.KeepAlive(twoFields);
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

    private static void AssertUncachedRecord(SandboxType[] fieldTypes)
    {
        var first = CompiledRuntime.TypeRecordCached(fieldTypes);
        var second = CompiledRuntime.TypeRecordCached(fieldTypes);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    private static void AssertSameRecordFailure(SandboxType[] fieldTypes)
    {
        var expected = RecordFailure(() => SandboxType.Record(fieldTypes));
        var actual = RecordFailure(() => CompiledRuntime.TypeRecordCached(fieldTypes));

        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(expected.ParamName, actual.ParamName);
    }

    private static ArgumentException RecordFailure(Func<SandboxType> create)
        => Assert.IsAssignableFrom<ArgumentException>(Assert.ThrowsAny<Exception>(() => create()));

    private static void AssertLegacyRecordUncached(SandboxType[] fieldTypes)
    {
        var first = CompiledRuntime.TypeRecord(fieldTypes);
        var second = CompiledRuntime.TypeRecord(fieldTypes);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }
}
