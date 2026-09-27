using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Audit;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class AuditFieldSnapshotAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("dictionary", 384)]
    [InlineData("read-only", 440)]
    [InlineData("enumerable", 440)]
    [InlineData("case-insensitive", 384)]
    [InlineData("empty", 248)]
    [InlineData("null", 128)]
    public void Public_event_copies_avoid_a_second_field_enumeration(string kind, int bytesPerCopy)
    {
        var input = AuditFieldSnapshotFixture.Input(kind, AuditFieldSnapshotFixture.Fields());
        var template = AuditFieldSnapshotFixture.Event(null);
        CopyEvents(template, input, 2_000);
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var result = CopyEvents(template, input, 10_000);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(input?.Count, result.Fields?.Count);
            Assert.NotSame(template, result);
        }

        output.WriteLine($"{kind}, public audit-event copy: {minimum / 10_000d} B/call");
        Assert.Equal(bytesPerCopy * 10_000L, minimum);
        Assert.Null(template.Fields);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static SandboxAuditEvent CopyEvents(
        SandboxAuditEvent template, IReadOnlyDictionary<string, string>? fields, int count)
    {
        var result = template;
        for (var index = 0; index < count; index++)
        {
            result = template with { Fields = fields };
        }
        return result;
    }
}
