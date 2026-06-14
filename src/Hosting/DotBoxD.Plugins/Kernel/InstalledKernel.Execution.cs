namespace DotBoxD.Plugins;

using DotBoxD.Hosting;
using DotBoxD.Kernels;

public sealed partial class InstalledKernel
{
    private ResourceMeter? _preparedValueBudget;

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
                ReusableNoAuditBudget(entrypoint))
            .ConfigureAwait(false);
        _executionObserver.Record(entrypoint, _executionMode, result);
        if (IsRevoked)
        {
            PluginKernelRevocation.ThrowIfRevoked(true);
        }

        if (!result.Succeeded)
        {
            throw new SandboxRuntimeException(result.Error ?? new SandboxError(SandboxErrorCode.HostFailure, "kernel execution failed"));
        }

        return result.Value ?? SandboxValue.Unit;
    }

    private ResourceMeter? ReusableNoAuditBudget(string entrypoint)
    {
        if (_executionMode != ExecutionMode.Compiled ||
            !_plan.BindingReferences.TryGetValue(entrypoint, out var bindings) ||
            bindings.Count != 0)
        {
            return null;
        }

        return _preparedValueBudget ??= new ResourceMeter(_plan.Budget);
    }
}
