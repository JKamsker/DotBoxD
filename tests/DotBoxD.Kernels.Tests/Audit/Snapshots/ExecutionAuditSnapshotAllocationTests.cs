using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Audit;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ExecutionAuditSnapshotAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("array", 152)]
    [InlineData("list", 152)]
    [InlineData("read-only-array", 152)]
    [InlineData("enumerable-list", 184)]
    [InlineData("owned-array", 88)]
    [InlineData("owned-list", 88)]
    [InlineData("empty", 88)]
    public void Public_result_copies_avoid_a_second_input_enumeration(string kind, int bytesPerCopy)
    {
        var input = AuditSnapshotFixture.Input(kind, AuditSnapshotFixture.Events());
        var template = AuditSnapshotFixture.Result([]);
        CopyResults(template, input, 2_000);
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var result = CopyResults(template, input, 10_000);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(input, result.AuditEvents);
            Assert.NotSame(template, result);
        }

        output.WriteLine($"{kind}, public execution-result copy: {minimum / 10_000d} B/call");
        Assert.Equal(bytesPerCopy * 10_000L, minimum);
        Assert.Empty(template.AuditEvents);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static SandboxExecutionResult CopyResults(
        SandboxExecutionResult template, IReadOnlyList<SandboxAuditEvent> events, int count)
    {
        var result = template;
        for (var index = 0; index < count; index++)
        {
            result = template with { AuditEvents = events };
        }
        return result;
    }
}
