using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

internal static class CompiledThreeArgumentBindingDispatcher
{
    public static SandboxValue CallBinding(
        SandboxContext context,
        string id,
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxValue arg2)
    {
        var descriptor = context.GetBindingDescriptor(id);
        var auditCheckpoint = context.AuditCheckpoint();
        using var grantClock = context.BeginBindingGrantClockScope(context.Policy.GrantClock);
        using var auditInvocation = context.BeginBindingAuditInvocation(descriptor, auditCheckpoint);
        try
        {
            CompiledBindingDispatcher.ValidateArguments(descriptor, arg0, arg1, arg2);
            context.ChargeBindingCall(descriptor);
            CompiledBindingDispatcher.EnsureAsyncGrant(context, descriptor);
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
            throw CompiledBindingDispatcher.BindingFailure(context, descriptor, auditInvocation);
        }

        var timeout = default(BindingWallTimeTokenLease);
        try
        {
            timeout = context.CreateBindingWallTimeToken();
            using var returnCredits = context.BeginBindingReturnCreditScope(descriptor.ReturnType);
            var pending = descriptor.Invoke.Target is IThreeArgumentBindingInvoker fastInvoker
                ? fastInvoker.Invoke(context, arg0, arg1, arg2, timeout.Token)
                : descriptor.Invoke(context, new[] { arg0, arg1, arg2 }, timeout.Token);
            var value = CompiledBindingDispatcher.AwaitBinding(context, pending, timeout.Token);
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
            throw CompiledBindingDispatcher.BindingFailure(context, descriptor, auditInvocation);
        }
        finally
        {
            timeout.Dispose();
        }
    }
}
