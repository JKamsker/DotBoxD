using System.Collections.Concurrent;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Core.ValueShapeCaching;

public sealed class ValueShapeCacheInvalidationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void ResetOwnedValues_invalidates_each_previously_measured_shape()
    {
        var value = ListValue.FromOwnedValues(
            [SandboxValue.FromString("a")],
            SandboxType.String);
        _ = ValueShapeCache.GetOrMeasure(value);

        value.ResetOwnedValues([SandboxValue.FromString("a much longer value")]);

        AssertMatchesFullMeasurement(value);

        value.ResetOwnedValues([SandboxValue.FromString("b")]);

        AssertMatchesFullMeasurement(value);
    }

    [Fact]
    public void Resource_meter_charges_the_reset_owned_list_shape()
    {
        var value = ListValue.FromOwnedValues(
            [NestedStringList("a")],
            SandboxType.List(SandboxType.String));
        _ = ValueShapeCache.GetOrMeasure(value);

        value.ResetOwnedValues([NestedStringList("a much longer value")]);
        var meter = new ResourceMeter(new ResourceLimits());
        meter.ChargeValue(value);

        Assert.Equal(38, meter.StringBytes);
        Assert.Equal(2, meter.CollectionElements);
    }

    [Fact]
    public void Reset_on_another_thread_invalidates_that_threads_hot_entry()
    {
        var value = ListValue.FromOwnedValues(
            [SandboxValue.FromString("a")],
            SandboxType.String);
        using var cached = new ManualResetEventSlim();
        using var reset = new ManualResetEventSlim();
        ShapeInfo afterReset = default;
        Exception? workerError = null;
        var worker = new Thread(() =>
        {
            try
            {
                _ = ValueShapeCache.GetOrMeasure(value);
                cached.Set();
                Assert.True(reset.Wait(TestTimeout));
                afterReset = ValueShapeCache.GetOrMeasure(value);
            }
            catch (Exception ex)
            {
                workerError = ex;
                cached.Set();
            }
        });

        worker.Start();
        Assert.True(cached.Wait(TestTimeout));
        value.ResetOwnedValues([SandboxValue.FromString("a much longer value")]);
        reset.Set();
        Assert.True(worker.Join(TestTimeout));

        Assert.Null(workerError);
        var measured = SandboxValueShapeMeter.MeasureWithNodes(value);
        Assert.Equal(measured.Nodes, afterReset.Nodes);
        Assert.Equal(measured.Shape, afterReset.Shape);
    }

    [Fact]
    public void Immutable_value_supports_parallel_measurement()
    {
        var value = SandboxValue.FromList(
            [NestedStringList("alpha"), NestedStringList("beta")],
            SandboxType.List(SandboxType.String));
        var measured = SandboxValueShapeMeter.MeasureWithNodes(value);
        var errors = new ConcurrentQueue<ShapeInfo>();

        Parallel.For(0, 256, _ =>
        {
            var cached = ValueShapeCache.GetOrMeasure(value);
            if (cached.Nodes != measured.Nodes || cached.Shape != measured.Shape)
            {
                errors.Enqueue(cached);
            }
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void Hot_entry_does_not_cross_threads()
    {
        var target = SandboxValue.FromList(
            [NestedStringList("a much longer value")],
            SandboxType.List(SandboxType.String));
        var donor = ValueShapeCache.GetOrMeasure(
            SandboxValue.FromList(
                [NestedStringList("a")],
                SandboxType.List(SandboxType.String)));
        Exception? workerError = null;
        var worker = new Thread(() =>
        {
            try
            {
                ValueShapeHotCache.Set(target, donor);
            }
            catch (Exception ex)
            {
                workerError = ex;
            }
        });

        worker.Start();
        Assert.True(worker.Join(TestTimeout));

        Assert.Null(workerError);
        AssertMatchesFullMeasurement(target);
    }

    [Fact]
    public void Map_with_replaced_values_does_not_inherit_the_original_shape()
    {
        var original = new MapValue(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromInt32(1)] = SandboxValue.FromString("a")
            },
            SandboxType.I32,
            SandboxType.String);
        _ = ValueShapeCache.GetOrMeasure(original);

        var updated = original with
        {
            Values = new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromInt32(1)] = SandboxValue.FromString("a much longer value")
            }
        };

        AssertMatchesFullMeasurement(updated);
    }

    private static ListValue NestedStringList(string value) =>
        (ListValue)SandboxValue.FromList(
            [SandboxValue.FromString(value)],
            SandboxType.String);

    private static void AssertMatchesFullMeasurement(SandboxValue value)
    {
        var cached = ValueShapeCache.GetOrMeasure(value);
        var measured = SandboxValueShapeMeter.MeasureWithNodes(value);
        Assert.Equal(measured.Nodes, cached.Nodes);
        Assert.Equal(measured.Shape, cached.Shape);
    }
}
