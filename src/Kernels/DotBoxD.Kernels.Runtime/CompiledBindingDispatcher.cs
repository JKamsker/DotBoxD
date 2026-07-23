using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

using DotBoxD.Kernels;

internal static partial class CompiledBindingDispatcher
{
    [ThreadStatic] private static ICompiledAwaitPump? _pump;

    internal static IDisposable InstallAwaitPump(ICompiledAwaitPump pump)
    {
        var previous = _pump;
        _pump = pump;
        return new AwaitPumpScope(previous);
    }

    public static SandboxValue CallBinding(SandboxContext context, string id, SandboxValue[] args)
    {
        var descriptor = context.GetBindingDescriptor(id);
        var auditCheckpoint = context.AuditCheckpoint(descriptor);
        using var grantClock = context.BeginBindingGrantClockScope(context.Policy.GrantClock);
        using var auditInvocation = context.BeginBindingAuditInvocation(descriptor, auditCheckpoint);
        try
        {
            ValidateArguments(descriptor, args);
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
            var value = AwaitBinding(context, descriptor.Invoke(context, args, timeout.Token), timeout.Token);
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
            var error = new SandboxError(SandboxErrorCode.Timeout, $"binding '{id}' timed out");
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

    public static SandboxValue CallBinding1(
        SandboxContext context,
        string id,
        SandboxValue arg0)
    {
        var descriptor = context.GetBindingDescriptor(id);
        var auditCheckpoint = context.AuditCheckpoint(descriptor);
        using var grantClock = context.BeginBindingGrantClockScope(context.Policy.GrantClock);
        using var auditInvocation = context.BeginBindingAuditInvocation(descriptor, auditCheckpoint);
        try
        {
            ValidateArguments(descriptor, arg0);
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
            var pending = descriptor.Invoke.Target is IOneArgumentBindingInvoker fastInvoker
                ? fastInvoker.Invoke(context, arg0, timeout.Token)
                : descriptor.Invoke(context, [arg0], timeout.Token);
            var value = AwaitBinding(context, pending, timeout.Token);
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
            var error = new SandboxError(SandboxErrorCode.Timeout, $"binding '{id}' timed out");
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

    public static SandboxValue CallBinding2(
        SandboxContext context,
        string id,
        SandboxValue arg0,
        SandboxValue arg1)
    {
        var descriptor = context.GetBindingDescriptor(id);
        var auditCheckpoint = context.AuditCheckpoint(descriptor);
        using var grantClock = context.BeginBindingGrantClockScope(context.Policy.GrantClock);
        using var auditInvocation = context.BeginBindingAuditInvocation(descriptor, auditCheckpoint);
        try
        {
            ValidateArguments(descriptor, arg0, arg1);
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
            var pending = descriptor.Invoke.Target is ITwoArgumentBindingInvoker fastInvoker
                ? fastInvoker.Invoke(context, arg0, arg1, timeout.Token)
                : descriptor.Invoke(context, new[] { arg0, arg1 }, timeout.Token);
            var value = AwaitBinding(context, pending, timeout.Token);
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
            var error = new SandboxError(SandboxErrorCode.Timeout, $"binding '{id}' timed out");
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

    internal static void EnsureAsyncGrant(SandboxContext context, BindingDescriptor descriptor)
    {
        if (!descriptor.IsAsync || context.AsyncEnabled)
        {
            return;
        }

        throw new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.PermissionDenied,
            $"binding '{descriptor.Id}' requires the '{RuntimeCapabilityIds.Async}' capability"));
    }

    internal static SandboxRuntimeException BindingFailure(
        SandboxContext context,
        BindingDescriptor descriptor,
        BindingAuditInvocation auditInvocation)
    {
        var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{descriptor.Id}' failed");
        context.EnsureRequiredBindingFailureAudit(descriptor, auditInvocation, error.Code);
        return new SandboxRuntimeException(error);
    }

    internal static SandboxValue AwaitBinding(
        SandboxContext context,
        ValueTask<SandboxValue> pending,
        CancellationToken timeoutToken)
    {
        // Synchronous bindings (the common case) complete inline, so read the
        // result directly and avoid allocating a Task<SandboxValue> wrapper.
        if (pending.IsCompleted)
        {
            return pending.GetAwaiter().GetResult();
        }

        if (!context.AsyncEnabled)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.BindingFailure,
                "binding returned a pending result; async capability is not granted"));
        }

        var pump = _pump;
        if (pump is null)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.BindingFailure,
                "async pump is not installed"));
        }

        return pump.RunToCompletion(pending, timeoutToken);
    }

    private sealed class AwaitPumpScope(ICompiledAwaitPump? previous) : IDisposable
    {
        public void Dispose()
        {
            _pump = previous;
        }
    }

}

internal interface ICompiledAwaitPump
{
    SandboxValue RunToCompletion(ValueTask<SandboxValue> pending, CancellationToken cancellationToken);
}
