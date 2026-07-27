using System.Diagnostics;
using System.Runtime.ExceptionServices;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff;

internal static class ValueShapeHandoffProbeSupport
{
    public const int CollectionSize = 256;
    public const int ListNodes = CollectionSize + 2;
    public const int MapNodes = (CollectionSize * 2) + 2;

    public static void MeasureCharge(
        SandboxContext context,
        SandboxValue value,
        ref long elapsedTicks,
        ref long allocatedBytes)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        context.ChargeValue(value);
        elapsedTicks += Stopwatch.GetTimestamp() - startedAt;
        allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    public static long ValidateList(ListValue value)
    {
        var info = ValueShapeCache.GetOrMeasure(value);
        if (value.Count != CollectionSize ||
            info.Nodes != ListNodes ||
            info.Shape.Elements != CollectionSize ||
            info.Shape.MaxListLength != CollectionSize ||
            info.Shape.MaxMapEntries != 0 ||
            info.Shape.Depth != 1 ||
            info.Shape.MaxStringLength != 0 ||
            info.Shape.StringBytes != 0)
        {
            throw new InvalidOperationException("List handoff shape changed.");
        }

        return info.Nodes + info.Shape.Elements + info.Shape.MaxListLength + info.Shape.Depth +
            value.Count + ((I32Value)value[0]).Value + ((I32Value)value[^1]).Value;
    }

    public static long ValidateMap(MapValue value, SandboxValue key)
    {
        var info = ValueShapeCache.GetOrMeasure(value);
        if (value.Values.Count != CollectionSize ||
            info.Nodes != MapNodes ||
            info.Shape.Elements != CollectionSize ||
            info.Shape.MaxListLength != 0 ||
            info.Shape.MaxMapEntries != CollectionSize ||
            info.Shape.Depth != 1 ||
            info.Shape.MaxStringLength != 0 ||
            info.Shape.StringBytes != 0 ||
            !value.Values.TryGetValue(key, out var replaced))
        {
            throw new InvalidOperationException("Map handoff shape changed.");
        }

        return info.Nodes + info.Shape.Elements + info.Shape.MaxMapEntries + info.Shape.Depth +
            value.Values.Count + ((I32Value)replaced).Value;
    }

    public static Measurement Capture(
        int iterations,
        long elapsedTicks,
        long allocatedBytes,
        long checksum,
        SandboxContext context,
        bool isList)
    {
        var expectedChecksum = (isList ? 1_029L : 2_283L) * iterations + (iterations / 2);
        var nodes = isList ? ListNodes : MapNodes;
        var usage = context.Budget.Snapshot();
        if (checksum != expectedChecksum ||
            usage.FuelUsed != (long)(nodes / 64) * iterations ||
            usage.CollectionElements != (long)CollectionSize * iterations ||
            usage.AllocatedBytes != 0 ||
            usage.StringBytes != 0)
        {
            throw new InvalidOperationException(
                $"Handoff invariants changed: checksum={checksum}/{expectedChecksum}, " +
                $"fuel={usage.FuelUsed}/{(long)(nodes / 64) * iterations}, " +
                $"elements={usage.CollectionElements}/{(long)CollectionSize * iterations}, " +
                $"allocation={usage.AllocatedBytes}, strings={usage.StringBytes}.");
        }

        return new Measurement(iterations, elapsedTicks, allocatedBytes, checksum);
    }

    public static ListValue CreateListSource(SandboxContext context)
    {
        var item = SandboxValue.FromInt32(1);
        var value = CompiledRuntime.ListEmpty(context, SandboxType.I32);
        for (var index = 1; index < CollectionSize; index++)
        {
            value = CompiledRuntime.ListAdd(context, value, item);
        }

        return (ListValue)value;
    }

    public static MapValue CreateMapSource(SandboxContext context)
    {
        var value = CompiledRuntime.MapEmpty(context, SandboxType.I32, SandboxType.I32);
        for (var index = 0; index < CollectionSize; index++)
        {
            value = CompiledRuntime.MapSet(
                context,
                value,
                SandboxValue.FromInt32(index),
                SandboxValue.FromInt32(index));
        }

        return (MapValue)value;
    }

    public static SandboxContext CreateContext(int maxListLength, int maxMapEntries)
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxWallTime: TimeSpan.FromMinutes(5),
            MaxAllocatedBytes: long.MaxValue,
            MaxListLength: maxListLength,
            MaxMapEntries: maxMapEntries,
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

    public static T RunOnDedicatedThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception caught)
            {
                error = caught;
            }
        })
        {
            IsBackground = true,
            Name = "ValueShapeCache handoff probe",
        };
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result;
    }

    public static void Write(string name, Measurement measurement)
    {
        var nanoseconds = measurement.ElapsedTicks *
            (1_000_000_000d / Stopwatch.Frequency) / measurement.Iterations;
        Console.WriteLine(
            $"{name,-43} {nanoseconds,10:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.AllocatedBytes / (double)measurement.Iterations,8:N1} B/op " +
            $"checksum={measurement.Checksum:N0}");
    }

    public readonly record struct Measurement(
        int Iterations,
        long ElapsedTicks,
        long AllocatedBytes,
        long Checksum);
}
