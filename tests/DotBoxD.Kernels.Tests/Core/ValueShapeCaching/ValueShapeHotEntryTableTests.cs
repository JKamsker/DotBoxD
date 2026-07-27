using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Core.ValueShapeCaching;

public sealed class ValueShapeHotEntryTableTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Bounded_table_finds_registered_entries_and_falls_back_beyond_capacity()
    {
        var table = new ValueShapeHotEntryTable(capacity: 2, directoryCapacity: 256);
        var first = CreateEntry("first");
        var second = CreateEntry("second");
        var fallback = CreateEntry("fallback");

        Assert.True(table.TryRegister(first.Entry));
        table.Publish(first.Entry, first.Value);
        Assert.True(table.TryGet(
            first.Value,
            first.Value.ShapeGeneration,
            localEntry: null,
            out var firstInfo));
        Assert.Equal(first.Info, firstInfo);
        Assert.False(table.TryGet(
            first.Value,
            first.Value.ShapeGeneration,
            localEntry: first.Entry,
            out _));

        Assert.True(table.TryRegister(second.Entry));
        table.Publish(second.Entry, second.Value);
        Assert.True(table.TryGet(
            second.Value,
            second.Value.ShapeGeneration,
            localEntry: null,
            out var secondInfo));
        Assert.Equal(second.Info, secondInfo);
        Assert.False(table.TryRegister(fallback.Entry));
        table.Publish(fallback.Entry, fallback.Value);
        Assert.False(table.TryGet(
            fallback.Value,
            fallback.Value.ShapeGeneration,
            localEntry: null,
            out _));
    }

    [Fact]
    public void Directory_collision_uses_bounded_discovery_without_returning_the_wrong_shape()
    {
        var table = new ValueShapeHotEntryTable(capacity: 2, directoryCapacity: 1);
        var first = CreateEntry("first");
        var second = CreateEntry("a much longer second value");

        Assert.True(table.TryRegister(first.Entry));
        table.Publish(first.Entry, first.Value);
        Assert.True(table.TryRegister(second.Entry));
        table.Publish(second.Entry, second.Value);

        Assert.True(table.TryGet(
            first.Value,
            first.Value.ShapeGeneration,
            localEntry: null,
            out var firstInfo));
        Assert.Equal(first.Info, firstInfo);
        Assert.True(table.TryGet(
            second.Value,
            second.Value.ShapeGeneration,
            localEntry: null,
            out var secondInfo));
        Assert.Equal(second.Info, secondInfo);
    }

    [Fact]
    public void Published_owned_list_shape_is_rejected_after_generation_reset()
    {
        var value = ListValue.FromOwnedValues(
            [SandboxValue.FromString("short")],
            SandboxType.String);
        var info = ValueShapeCache.GetOrMeasure(value);
        var entry = new ValueShapeHotEntry(value, value.ShapeGeneration, info);
        entry.EnableCrossThreadPublication();

        value.ResetOwnedValues([SandboxValue.FromString("a much longer value")]);

        Assert.False(entry.TryGetPublished(value, value.ShapeGeneration, out _));
    }

    [Fact]
    public void Concurrent_publication_never_returns_another_values_shape()
    {
        var first = CreateEntry("a");
        var secondValue = (ListValue)SandboxValue.FromList(
            [SandboxValue.FromString("a much longer value")],
            SandboxType.String);
        var secondInfo = ValueShapeCache.GetOrMeasure(secondValue);
        first.Entry.EnableCrossThreadPublication();
        using var start = new ManualResetEventSlim();
        Exception? error = null;
        var hits = 0;
        var producer = new Thread(() =>
        {
            start.Wait();
            for (var iteration = 0; iteration < 100_000; iteration++)
            {
                var useFirst = (iteration & 1) == 0;
                first.Entry.Publish(
                    useFirst ? first.Value : secondValue,
                    useFirst ? first.Value.ShapeGeneration : secondValue.ShapeGeneration,
                    useFirst ? first.Info : secondInfo);
            }
        });
        var consumer = new Thread(() =>
        {
            try
            {
                start.Wait();
                for (var iteration = 0; iteration < 100_000; iteration++)
                {
                    AssertPublishedShape(first.Entry, first.Value, first.Info, ref hits);
                    AssertPublishedShape(first.Entry, secondValue, secondInfo, ref hits);
                }
            }
            catch (Exception caught)
            {
                error = caught;
            }
        });

        producer.Start();
        consumer.Start();
        start.Set();
        Assert.True(producer.Join(TestTimeout));
        Assert.True(consumer.Join(TestTimeout));
        Assert.Null(error);
        Assert.True(hits > 0);
    }

    [Fact]
    public void Table_releases_collected_owner_entry_and_reuses_its_slot()
    {
        var table = new ValueShapeHotEntryTable(capacity: 1, directoryCapacity: 1);
        var weakEntry = RegisterTransientEntry(table);

        Collect();

        Assert.False(weakEntry.TryGetTarget(out _));
        Assert.True(table.TryRegister(CreateEntry("replacement").Entry));
    }

    [Fact]
    public void Reused_slot_does_not_make_a_stale_directory_token_authoritative()
    {
        var table = new ValueShapeHotEntryTable(capacity: 1, directoryCapacity: 1);
        var oldValue = (ListValue)SandboxValue.FromList(
            [SandboxValue.FromString("old")],
            SandboxType.String);
        var weakEntry = RegisterAndPublishTransientEntry(table, oldValue);

        Collect();

        Assert.False(weakEntry.TryGetTarget(out _));
        Assert.True(table.TryRegister(CreateEntry("replacement").Entry));
        Assert.False(table.TryGet(
            oldValue,
            oldValue.ShapeGeneration,
            localEntry: null,
            out _));
    }

    [Fact]
    public void Published_entry_does_not_retain_its_value()
    {
        var (entry, weakValue) = CreateEntryWithWeakValue();

        Collect();

        Assert.False(weakValue.TryGetTarget(out _));
        GC.KeepAlive(entry);
    }

    private static void AssertPublishedShape(
        ValueShapeHotEntry entry,
        ListValue value,
        ShapeInfo expected,
        ref int hits)
    {
        if (entry.TryGetPublished(value, value.ShapeGeneration, out var observed))
        {
            Assert.Equal(expected, observed);
            hits++;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ValueShapeHotEntry> RegisterTransientEntry(
        ValueShapeHotEntryTable table)
    {
        var entry = CreateEntry("transient").Entry;
        Assert.True(table.TryRegister(entry));
        return new WeakReference<ValueShapeHotEntry>(entry);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ValueShapeHotEntry> RegisterAndPublishTransientEntry(
        ValueShapeHotEntryTable table,
        ListValue value)
    {
        var info = ValueShapeCache.GetOrMeasure(value);
        var entry = new ValueShapeHotEntry(value, value.ShapeGeneration, info);
        Assert.True(table.TryRegister(entry));
        table.Publish(entry, value);
        return new WeakReference<ValueShapeHotEntry>(entry);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ValueShapeHotEntry Entry, WeakReference<SandboxValue> Value) CreateEntryWithWeakValue()
    {
        var value = (ListValue)SandboxValue.FromList(
            [SandboxValue.FromString("collectible")],
            SandboxType.String);
        var info = ValueShapeCache.GetOrMeasure(value);
        var entry = new ValueShapeHotEntry(value, value.ShapeGeneration, info);
        entry.EnableCrossThreadPublication();
        return (entry, new WeakReference<SandboxValue>(value));
    }

    private static TestEntry CreateEntry(string text)
    {
        var value = (ListValue)SandboxValue.FromList(
            [SandboxValue.FromString(text)],
            SandboxType.String);
        var info = ValueShapeCache.GetOrMeasure(value);
        return new TestEntry(
            value,
            info,
            new ValueShapeHotEntry(value, value.ShapeGeneration, info));
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record TestEntry(ListValue Value, ShapeInfo Info, ValueShapeHotEntry Entry);
}
