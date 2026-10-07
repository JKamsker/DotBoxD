using DotBoxD.Kernels.Benchmarks.Measurements;
using System.Collections;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Core.Collections;

// A deliberately non-shipping storage prototype: measure what packing alone saves and
// what the existing SandboxValue boundary costs before changing ownership or metering.
internal static class PrimitivePackingProbe
{
    private static object? _retained;
    private static long _sum;

    public static void Run()
    {
        Console.WriteLine("I64 packing prototype: construction and one complete indexed read; five samples; no runtime integration.");
        foreach (var count in new[] { 32, 256, 4096, 65536 })
        {
            var source = Enumerable.Range(0, count).Select(i => (long)i + 1000).ToArray();
            var iterations = Math.Max(16, 1_000_000 / count);
            ScalingProbeMeter.Measure($"{count}/generic/construct", () => _retained = BuildGeneric(source), iterations);
            ScalingProbeMeter.Measure($"{count}/packed/construct", () => _retained = new Packed(source), iterations);
            var generic = BuildGeneric(source);
            var packed = new Packed(source);
            ScalingProbeMeter.Measure($"{count}/generic/indexed-sum", () => _sum = Sum(generic), iterations);
            ScalingProbeMeter.Measure($"{count}/packed/indexed-sum", () => _sum = Sum(packed), iterations);
            ScalingProbeMeter.Measure($"{count}/packed/public-list-boundary",
                () => _retained = SandboxValue.FromList(packed, SandboxType.I64), iterations);
            if (Sum(generic) != Sum(packed))
            {
                throw new InvalidOperationException("Packing changed values.");
            }
        }

        Console.WriteLine($"checksum: {_sum}; retained: {_retained?.GetType().Name}");
    }

    private static ListValue BuildGeneric(long[] values)
    {
        var wrappers = new SandboxValue[values.Length];
        for (var i = 0; i < wrappers.Length; i++)
        {
            wrappers[i] = SandboxValue.FromInt64(values[i]);
        }

        return (ListValue)SandboxValue.FromList(wrappers, SandboxType.I64);
    }

    private static long Sum(IReadOnlyList<SandboxValue> values)
    {
        var sum = 0L;
        for (var i = 0; i < values.Count; i++)
        {
            sum += ((I64Value)values[i]).Value;
        }

        return sum;
    }

    private sealed class Packed(long[] source) : IReadOnlyList<SandboxValue>
    {
        private readonly long[] _values = source.ToArray();
        public int Count => _values.Length;
        public SandboxValue this[int index] => SandboxValue.FromInt64(_values[index]);
        public IEnumerator<SandboxValue> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
