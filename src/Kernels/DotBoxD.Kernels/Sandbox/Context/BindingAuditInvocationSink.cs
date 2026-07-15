using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Sandbox;

/// <summary>
/// Serializes terminal audit writes with the runtime's required-audit decision for one
/// async binding invocation. The current invocation flows into the binding's async
/// continuation so a terminal write that loses to the runtime fallback can be rejected.
/// </summary>
internal sealed class BindingAuditInvocationSink : IAuditSink
{
    private static readonly AsyncLocal<BindingAuditInvocationSink?> CurrentInvocation = new();

    private readonly object _gate = new();
    private readonly SandboxContext _context;
    private readonly InMemoryAuditSink _destination;
    private readonly BindingDescriptor _descriptor;
    private readonly long _checkpoint;
    private readonly BindingAuditInvocationSink? _previous;
    private bool _sealed;
    private bool _suppressSuccess;
    private SandboxErrorCode? _suppressedFailureCode;

    internal BindingAuditInvocationSink(
        SandboxContext context,
        InMemoryAuditSink destination,
        BindingDescriptor descriptor,
        long checkpoint)
    {
        _context = context;
        _destination = destination;
        _descriptor = descriptor;
        _checkpoint = checkpoint;
        _previous = CurrentInvocation.Value;
        CurrentInvocation.Value = this;
    }

    public long EventsWritten
    {
        get
        {
            lock (_gate)
            {
                return _destination.EventsWritten;
            }
        }
    }

    public void Write(SandboxAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        lock (_gate)
        {
            if (!IsSuppressedTerminal(auditEvent))
            {
                _destination.Write(auditEvent);
            }
        }
    }

    public bool HasBindingAuditSince(
        BindingDescriptor descriptor,
        long checkpoint,
        bool success,
        SandboxErrorCode? expectedErrorCode,
        SandboxRunId runId,
        string moduleHash,
        string policyHash)
    {
        lock (_gate)
        {
            return _destination.HasBindingAuditSince(
                descriptor,
                checkpoint,
                success,
                expectedErrorCode,
                runId,
                moduleHash,
                policyHash);
        }
    }

    internal static BindingAuditInvocationSink? CurrentFor(SandboxContext context)
    {
        for (var current = CurrentInvocation.Value; current is not null; current = current._previous)
        {
            if (ReferenceEquals(current._context, context))
            {
                return current;
            }
        }

        return null;
    }

    internal bool TrySealSuccess()
    {
        lock (_gate)
        {
            var hasRequiredAudit = HasRequiredAudit(success: true, null);
            Seal(success: true, null);
            return hasRequiredAudit;
        }
    }

    internal void EnsureFailure(SandboxErrorCode errorCode)
    {
        lock (_gate)
        {
            if (!HasRequiredAudit(success: false, errorCode))
            {
                _destination.Write(_context.CreateRequiredBindingFailureAudit(_descriptor, errorCode));
            }

            Seal(success: false, errorCode);
        }
    }

    internal void Exit()
    {
        if (ReferenceEquals(CurrentInvocation.Value, this))
        {
            CurrentInvocation.Value = _previous;
        }
    }

    private bool HasRequiredAudit(bool success, SandboxErrorCode? errorCode)
        => _destination.HasBindingAuditSince(
            _descriptor,
            _checkpoint,
            success,
            errorCode,
            _context.RunId,
            _context.ModuleHash,
            _context.PolicyHash);

    private bool IsSuppressedTerminal(SandboxAuditEvent auditEvent)
    {
        if (!_sealed)
        {
            return false;
        }

        if (_suppressSuccess && BindingAuditMatches(auditEvent, success: true, null))
        {
            return true;
        }

        return _suppressedFailureCode is { } errorCode &&
               BindingAuditMatches(auditEvent, success: false, errorCode);
    }

    private bool BindingAuditMatches(
        SandboxAuditEvent auditEvent,
        bool success,
        SandboxErrorCode? errorCode)
        => InMemoryAuditSink.BindingAuditMatches(
            auditEvent,
            _descriptor,
            success,
            errorCode,
            _context.RunId,
            _context.ModuleHash,
            _context.PolicyHash);

    private void Seal(bool success, SandboxErrorCode? errorCode)
    {
        if (success)
        {
            _suppressSuccess = true;
        }
        else
        {
            _suppressedFailureCode = errorCode;
        }

        _sealed = true;
    }
}

internal readonly struct BindingAuditInvocation(
    long checkpoint,
    BindingAuditInvocationSink? sink = null) : IDisposable
{
    internal long Checkpoint { get; } = checkpoint;
    internal BindingAuditInvocationSink? Sink { get; } = sink;

    public void Dispose() => Sink?.Exit();
}
