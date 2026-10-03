using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Replay;

internal sealed class BindingTraceRecorder
{
    private readonly object _gate = new();
    private readonly List<RecordedBindingCall> _calls = [];
    private readonly List<(long Start, long End)> _auditRanges = [];
    private bool _closed;

    public int Begin(string bindingId, IReadOnlyList<SandboxValue> arguments, long auditStart)
    {
        var captured = arguments.Select(TraceValue.Capture).ToArray();
        lock (_gate)
        {
            if (_closed || _calls.Count >= 100_000)
            {
                throw new InvalidDataException("Execution recording is closed or exceeds its call limit.");
            }
            var index = _calls.Count;
            _calls.Add(new RecordedBindingCall(bindingId, captured, null, null, false) { Completed = false });
            _auditRanges.Add((auditStart, auditStart));
            return index;
        }
    }

    public void Complete(int index, TraceValue? result, SandboxError? error, bool cancelled, long auditEnd)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return; // A timed-out, noncooperative binding cannot mutate the published trace.
            }
            _calls[index] = _calls[index] with { Result = result, Error = error, CancellationRequested = cancelled, Completed = true };
            _auditRanges[index] = (_auditRanges[index].Start, auditEnd);
        }
    }

    public IReadOnlyList<RecordedBindingCall> Finish(IReadOnlyList<SandboxAuditEvent> audit)
    {
        lock (_gate)
        {
            _closed = true;
            return Array.AsReadOnly(_calls.Select((call, index) => call with
            {
                AuditEvents = audit.Where(item => item.SequenceNumber > _auditRanges[index].Start &&
                    item.SequenceNumber <= _auditRanges[index].End).ToArray()
            }).ToArray());
        }
    }
}
