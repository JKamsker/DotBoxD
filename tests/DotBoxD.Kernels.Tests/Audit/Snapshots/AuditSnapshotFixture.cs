using System.Collections;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Audit;

internal static class AuditSnapshotFixture
{
    public static SandboxAuditEvent[] Events()
    {
        var runId = SandboxRunId.New();
        return
        [
            new(runId, "first", DateTimeOffset.UnixEpoch, true) { SequenceNumber = 1 },
            new(runId, "second", DateTimeOffset.UnixEpoch, true) { SequenceNumber = 2 },
        ];
    }

    public static IReadOnlyList<SandboxAuditEvent> Input(string kind, SandboxAuditEvent[] events)
        => kind switch
        {
            "array" => events,
            "list" => events.ToList(),
            "read-only-array" => Array.AsReadOnly(events),
            "enumerable-list" => new CountingList(events),
            "owned-array" => new OwnedAuditEventSnapshot(events),
            "owned-list" => new OwnedAuditEventSnapshot(events.ToList()),
            "empty" => Array.Empty<SandboxAuditEvent>(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public static SandboxExecutionResult Result(IReadOnlyList<SandboxAuditEvent> events)
        => new()
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = new(0, 1_000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            AuditEvents = events,
            ModuleHash = "module",
            PlanHash = "plan",
            PolicyHash = "policy",
        };

    internal sealed class CountingList(SandboxAuditEvent[] events) : IReadOnlyList<SandboxAuditEvent>
    {
        public int Enumerations { get; private set; }
        public int Count => events.Length;
        public SandboxAuditEvent this[int index] => events[index];

        public IEnumerator<SandboxAuditEvent> GetEnumerator()
        {
            Enumerations++;
            return ((IEnumerable<SandboxAuditEvent>)events).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class ThrowingList(SandboxAuditEvent[] events, Exception failure) : IReadOnlyList<SandboxAuditEvent>
    {
        public int Count => events.Length;
        public SandboxAuditEvent this[int index] => events[index];

        public IEnumerator<SandboxAuditEvent> GetEnumerator()
        {
            yield return events[0];
            throw failure;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
