using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

/// <summary>
/// Invokes a host binding from the interpreter call path, preserving the exact
/// audit-checkpoint, fuel/return charging, wall-time timeout, and failure-audit
/// ordering. Extracted from <see cref="ExpressionEvaluator"/> verbatim so the call
/// dispatcher stays focused while this security-sensitive control flow stays in one
/// cohesive place.
/// </summary>
internal static class InterpreterBindingCaller
{
    /// <summary>
    /// Invokes the host binding identified by <paramref name="descriptor"/>. The
    /// <paramref name="args"/> sequence is caller-owned and may be retained by the host
    /// binding, so it must be a stable, dedicated sequence (never a pooled or reused buffer).
    /// </summary>
    public static ValueTask<SandboxValue> CallAsync(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        BindingDescriptor descriptor,
        IReadOnlyList<SandboxValue> args,
        string functionId)
        => CallCoreAsync(
            context,
            options,
            moduleHash,
            descriptor,
            BindingInvocationArguments.FromList(args),
            functionId);

    public static ValueTask<SandboxValue> CallAsync(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        BindingDescriptor descriptor,
        SandboxValue arg0,
        string functionId)
        => CallCoreAsync(
            context,
            options,
            moduleHash,
            descriptor,
            BindingInvocationArguments.FromSingle(arg0),
            functionId);

    public static ValueTask<SandboxValue> CallAsync(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        BindingDescriptor descriptor,
        SandboxValue arg0,
        SandboxValue arg1,
        string functionId)
        => CallCoreAsync(
            context,
            options,
            moduleHash,
            descriptor,
            BindingInvocationArguments.FromPair(arg0, arg1),
            functionId);

    private static async ValueTask<SandboxValue> CallCoreAsync(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        BindingDescriptor descriptor,
        BindingInvocationArguments arguments,
        string functionId)
    {
        InterpreterTrace.WriteBindingCall(context, options, moduleHash, functionId, descriptor);
        var auditCheckpoint = context.AuditCheckpoint();
        using var grantClock = context.BeginBindingGrantClockScope(context.Policy.GrantClock);
        using var auditInvocation = context.BeginBindingAuditInvocation(descriptor, auditCheckpoint);
        try
        {
            context.ChargeBindingCall(descriptor);
            EnsureAsyncGrant(context, descriptor);
        }
        catch (SandboxRuntimeException ex)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditInvocation, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditInvocation, SandboxErrorCode.Cancelled);
            throw;
        }
        catch (Exception)
        {
            throw BindingFailure(context, descriptor, auditInvocation);
        }

        var timeout = default(BindingWallTimeTokenLease);
        try
        {
            timeout = context.CreateBindingWallTimeToken();
            using var returnCredits = context.BeginBindingReturnCreditScope(descriptor.ReturnType);
            var pending = arguments.Invoke(context, descriptor, timeout.Token);
            var value = pending.IsCompleted
                ? pending.GetAwaiter().GetResult()
                : await AwaitPendingAsync(context, pending, timeout.Token).ConfigureAwait(false);
            context.Checkpoint();
            value = context.ChargeBindingReturn(descriptor, value);
            context.EnsureRequiredBindingSuccessAudit(descriptor, auditInvocation);
            return value;
        }
        catch (SandboxRuntimeException ex)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditInvocation, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditInvocation, SandboxErrorCode.Cancelled);
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, $"binding '{descriptor.Id}' timed out");
            context.EnsureRequiredBindingFailureAudit(descriptor, auditInvocation, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            throw BindingFailure(context, descriptor, auditInvocation);
        }
        finally
        {
            timeout.Dispose();
        }
    }

    private static void EnsureAsyncGrant(SandboxContext context, BindingDescriptor descriptor)
    {
        if (!descriptor.IsAsync || context.AsyncEnabled)
        {
            return;
        }

        throw new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.PermissionDenied,
            $"binding '{descriptor.Id}' requires the '{RuntimeCapabilityIds.Async}' capability"));
    }

    private static SandboxRuntimeException BindingFailure(
        SandboxContext context,
        BindingDescriptor descriptor,
        BindingAuditInvocation auditInvocation)
    {
        var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{descriptor.Id}' failed");
        context.EnsureRequiredBindingFailureAudit(descriptor, auditInvocation, error.Code);
        return new SandboxRuntimeException(error);
    }

    private static async ValueTask<SandboxValue> AwaitPendingAsync(
        SandboxContext context,
        ValueTask<SandboxValue> pending,
        CancellationToken timeoutToken)
    {
        if (!context.AsyncEnabled)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.BindingFailure,
                "binding returned a pending result; async capability is not granted"));
        }

        return await pending.AsTask().WaitAsync(timeoutToken).ConfigureAwait(false);
    }

    private readonly struct BindingInvocationArguments
    {
        private readonly IReadOnlyList<SandboxValue>? _list;
        private readonly SandboxValue? _arg0;
        private readonly SandboxValue? _arg1;
        private readonly InvocationShape _shape;

        private BindingInvocationArguments(
            IReadOnlyList<SandboxValue>? list,
            SandboxValue? arg0,
            SandboxValue? arg1,
            InvocationShape shape)
        {
            _list = list;
            _arg0 = arg0;
            _arg1 = arg1;
            _shape = shape;
        }

        public static BindingInvocationArguments FromList(IReadOnlyList<SandboxValue> args)
            => new(args, null, null, InvocationShape.List);

        public static BindingInvocationArguments FromSingle(SandboxValue arg0)
            => new(null, arg0, null, InvocationShape.Single);

        public static BindingInvocationArguments FromPair(SandboxValue arg0, SandboxValue arg1)
            => new(null, arg0, arg1, InvocationShape.Pair);

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            BindingDescriptor descriptor,
            CancellationToken cancellationToken)
            => _shape switch
            {
                InvocationShape.Single when descriptor.Invoke.Target is IOneArgumentBindingInvoker invoker =>
                    invoker.Invoke(context, _arg0!, cancellationToken),
                InvocationShape.Pair when descriptor.Invoke.Target is ITwoArgumentBindingInvoker invoker =>
                    invoker.Invoke(context, _arg0!, _arg1!, cancellationToken),
                InvocationShape.Single => descriptor.Invoke(context, [_arg0!], cancellationToken),
                InvocationShape.Pair => descriptor.Invoke(context, [_arg0!, _arg1!], cancellationToken),
                _ => descriptor.Invoke(context, _list!, cancellationToken)
            };
    }

    private enum InvocationShape : byte
    {
        List,
        Single,
        Pair
    }
}
