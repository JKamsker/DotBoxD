using System.Reflection;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Audit;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class AuditObserverPublicationAllocationTests(ITestOutputHelper output)
{
    // Bind once outside measurement to isolate publication from execution and snapshot creation.
    private static readonly Func<SandboxHost, SandboxExecutionResult, SandboxExecutionResult> Publish =
        typeof(SandboxHost).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<SandboxHost, SandboxExecutionResult, SandboxExecutionResult>>();

    [Theory]
    [InlineData("copied", 0)]
    [InlineData("copied", 1)]
    [InlineData("copied", 4)]
    [InlineData("owned-array", 0)]
    [InlineData("owned-array", 1)]
    [InlineData("owned-array", 4)]
    [InlineData("owned-list", 0)]
    [InlineData("owned-list", 1)]
    [InlineData("owned-list", 4)]
    public void Publishing_an_existing_snapshot_does_not_allocate(string snapshotKind, int observerCount)
    {
        var runId = SandboxRunId.New();
        SandboxAuditEvent[] events =
        [
            new(runId, "first", DateTimeOffset.UtcNow, true) { SequenceNumber = 1 },
            new(runId, "second", DateTimeOffset.UtcNow, true) { SequenceNumber = 2 },
        ];
        IReadOnlyList<SandboxAuditEvent> snapshot = snapshotKind switch
        {
            "owned-array" => new OwnedAuditEventSnapshot(events),
            "owned-list" => new OwnedAuditEventSnapshot(events.ToList()),
            _ => events,
        };
        var result = new SandboxExecutionResult
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = new(0, 1_000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            AuditEvents = snapshot,
            ModuleHash = "module",
            PlanHash = "plan",
            PolicyHash = "policy",
        };
        var observers = Enumerable.Range(0, observerCount).Select(_ => new Observer(events)).ToArray();
        using var host = AuditObserverLifetimeFixture.Host(
            observers.Select(observer => (Action<SandboxAuditEvent>)observer.OnEvent).ToArray());
        Assert.Same(result, PublishMany(host, result, 2_000));
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var published = PublishMany(host, result, 10_000);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Same(result, published);
        }

        output.WriteLine($"{snapshotKind}, {observerCount} observers: {minimum / 10_000d} B/publication");
        Assert.All(observers, observer =>
        {
            Assert.Equal(104_000, observer.Calls);
            Assert.False(observer.OutOfOrder);
        });
        Assert.Equal(0, minimum);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static SandboxExecutionResult PublishMany(SandboxHost host, SandboxExecutionResult result, int count)
    {
        var published = result;
        for (var index = 0; index < count; index++)
        {
            published = Publish(host, result);
        }
        return published;
    }

    private sealed class Observer(SandboxAuditEvent[] expected)
    {
        public int Calls { get; private set; }
        public bool OutOfOrder { get; private set; }

        public void OnEvent(SandboxAuditEvent auditEvent)
        {
            OutOfOrder |= !ReferenceEquals(expected[Calls % expected.Length], auditEvent);
            Calls++;
        }
    }
}
