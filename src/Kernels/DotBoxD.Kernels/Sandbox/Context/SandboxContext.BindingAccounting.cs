using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext
{
    public long AuditCheckpoint() => Audit.EventsWritten;

    internal BindingAuditInvocation BeginBindingAuditInvocation(
        BindingDescriptor descriptor,
        long checkpoint)
    {
        var audit = _audit ?? InitializeAudit();
        if (descriptor.AuditLevel == AuditLevel.None ||
            !descriptor.IsAsync ||
            audit is not InMemoryAuditSink inMemoryAudit)
        {
            return new BindingAuditInvocation(checkpoint);
        }

        return new BindingAuditInvocation(
            checkpoint,
            new BindingAuditInvocationSink(this, inMemoryAudit, descriptor, checkpoint));
    }

    public void EnsureRequiredBindingSuccessAudit(BindingDescriptor descriptor, long checkpoint)
    {
        if (descriptor.AuditLevel == AuditLevel.None ||
            Audit.HasBindingAuditSince(descriptor, checkpoint, success: true, null, RunId, ModuleHash, PolicyHash))
        {
            return;
        }

        throw new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.BindingFailure,
            $"binding '{descriptor.Id}' did not emit a required audit event"));
    }

    internal void EnsureRequiredBindingSuccessAudit(
        BindingDescriptor descriptor,
        BindingAuditInvocation invocation)
    {
        if (descriptor.AuditLevel == AuditLevel.None)
        {
            return;
        }

        if (invocation.Sink is null)
        {
            EnsureRequiredBindingSuccessAudit(descriptor, invocation.Checkpoint);
            return;
        }

        if (!invocation.Sink.TrySealSuccess())
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.BindingFailure,
                $"binding '{descriptor.Id}' did not emit a required audit event"));
        }
    }

    public void EnsureRequiredBindingFailureAudit(
        BindingDescriptor descriptor,
        long checkpoint,
        SandboxErrorCode errorCode)
    {
        if (descriptor.AuditLevel == AuditLevel.None ||
            Audit.HasBindingAuditSince(descriptor, checkpoint, success: false, errorCode, RunId, ModuleHash, PolicyHash))
        {
            return;
        }

        Audit.Write(CreateRequiredBindingFailureAudit(descriptor, errorCode));
    }

    internal void EnsureRequiredBindingFailureAudit(
        BindingDescriptor descriptor,
        BindingAuditInvocation invocation,
        SandboxErrorCode errorCode)
    {
        if (descriptor.AuditLevel == AuditLevel.None)
        {
            return;
        }

        if (invocation.Sink is null)
        {
            EnsureRequiredBindingFailureAudit(descriptor, invocation.Checkpoint, errorCode);
            return;
        }

        invocation.Sink.EnsureFailure(errorCode);
    }

    internal SandboxAuditEvent CreateRequiredBindingFailureAudit(
        BindingDescriptor descriptor,
        SandboxErrorCode errorCode)
    {
        var timestamp = AuditTimestamp();
        return new SandboxAuditEvent(
            RunId,
            "BindingCall",
            timestamp,
            Success: false,
            BindingId: descriptor.Id,
            CapabilityId: descriptor.RequiredCapability,
            Effect: descriptor.Effects,
            ResourceId: $"binding:{descriptor.Id}",
            ErrorCode: errorCode,
            Message: "binding failed before emitting audit",
            Fields: BindingAuditFields("binding", timestamp));
    }

    public IReadOnlyDictionary<string, string> BindingAuditFields(
        string resourceKind,
        DateTimeOffset startedAt,
        long? bytesRead = null,
        long? bytesWritten = null)
        => Kernels.Bindings.BindingAuditFields.Create(
            resourceKind,
            startedAt,
            ModuleHash,
            PolicyHash,
            Policy.Deterministic,
            bytesRead,
            bytesWritten);

    public void ChargeBindingCall(BindingDescriptor descriptor)
    {
        if (AllowedBindingIds is not null && !AllowedBindingIds.Contains(descriptor.Id))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.ValidationError,
                $"binding '{descriptor.Id}' is not referenced by the verified execution plan"));
        }

        if (descriptor.RequiredCapability is not null)
        {
            RequireCapability(descriptor.RequiredCapability);
        }

        Budget.ChargeHostCall(descriptor.Id, descriptor.CostModel.MaxCallsPerRun);
        ChargeFuel(descriptor.CostModel.BaseFuel);
    }

    public SandboxValue ChargeBindingReturn(BindingDescriptor descriptor, SandboxValue value)
    {
        var shape = SandboxValidatedValueShapeMeter.MeasureBindingReturn(
            value,
            descriptor.ReturnType,
            descriptor.Id,
            Budget.Limits,
            CancellationToken,
            Budget);

        if (_returnCredits is null || !_returnCredits.TryConsume(value))
        {
            Budget.ChargeValueShape(shape);
        }

        if (shape.StringBytes > 0 && descriptor.CostModel.PerByteFuel > 0)
        {
            ChargeFuel(CheckedReturnFuel(shape.StringBytes, descriptor.CostModel.PerByteFuel));
        }

        return value;
    }

    private static long CheckedReturnFuel(long bytes, long perByteFuel)
    {
        try
        {
            return checked(bytes * perByteFuel);
        }
        catch (OverflowException)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "binding return fuel budget exhausted"));
        }
    }
}
