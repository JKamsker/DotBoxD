using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Plugins.Kernel;

public sealed partial class InstalledKernel
{
    private CompiledNoAuditRunState? _preparedValueState;

    private async ValueTask<SandboxValue> ExecutePreparedAsync(
        string entrypoint,
        SandboxValue input,
        CancellationToken cancellationToken)
    {
        using var executionCancellation = PluginExecutionCancellation.Create(
            cancellationToken,
            _revocation.Token);
        var result = await _host.ExecutePreparedValueInProcessAsync(
                _plan,
                entrypoint,
                input,
                _executionOptions,
                executionCancellation.Token,
                ReusableNoAuditState(entrypoint))
            .ConfigureAwait(false);
        var isRevoked = IsRevoked;
        var terminalResult = isRevoked ? WithRevokedError(result) : result;
        _executionObserver.Record(entrypoint, _executionMode, terminalResult);
        if (isRevoked)
        {
            PluginKernelRevocation.ThrowIfRevoked(true);
        }

        if (!terminalResult.Succeeded)
        {
            if (terminalResult.Error?.Code == SandboxErrorCode.Cancelled &&
                cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new SandboxRuntimeException(
                terminalResult.Error ?? new SandboxError(SandboxErrorCode.HostFailure, "kernel execution failed"));
        }

        return terminalResult.Value ?? SandboxValue.Unit;
    }

    private static PreparedExecutionResult WithRevokedError(PreparedExecutionResult result)
    {
        var error = PluginKernelRevocation.Error();
        return result with
        {
            Succeeded = false,
            Value = null,
            Error = error,
            FullResult = result.FullResult is null
                ? null
                : result.FullResult with
                {
                    Succeeded = false,
                    Value = null,
                    Error = error
                }
        };
    }

    private CompiledNoAuditRunState? ReusableNoAuditState(string entrypoint)
    {
        if (_executionMode != ExecutionMode.Compiled ||
            !_plan.BindingReferences.TryGetValue(entrypoint, out var bindings) ||
            bindings.Count != 0)
        {
            return null;
        }

        return _preparedValueState ??= new CompiledNoAuditRunState(_plan);
    }
}
