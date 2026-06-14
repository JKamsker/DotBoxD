namespace DotBoxD.Plugins;

using DotBoxD.Kernels;

public sealed partial class InstalledKernel
{
    private SandboxValue[]? _preparedInputValues;
    private ListValue? _preparedInputList;

    private SandboxValue BuildInput<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        string entrypoint)
    {
        var deferredUpdates = _liveStateSync.SynchronizeForInput();
        return UsesReusableNoAuditInput(entrypoint)
            ? PluginKernelInputBuilder.BuildWithReusableBuffer(
                adapter,
                e,
                deferredUpdates,
                Manifest.LiveSettings,
                Value,
                _pendingLiveUpdates.Enqueue,
                ref _preparedInputValues,
                ref _preparedInputList)
            : PluginKernelInputBuilder.Build(
                adapter,
                e,
                deferredUpdates,
                Manifest.LiveSettings,
                Value,
                _pendingLiveUpdates.Enqueue);
    }

    private bool UsesReusableNoAuditInput(string entrypoint)
        => _executionMode == ExecutionMode.Compiled &&
           string.Equals(entrypoint, _entrypoints.ShouldHandle, StringComparison.Ordinal) &&
           _plan.BindingReferences.TryGetValue(entrypoint, out var bindings) &&
           bindings.Count == 0;

    private static SandboxValue SnapshotInput(SandboxValue input)
        => input is ListValue list
            ? SandboxValue.FromList(list.Values, list.ItemType)
            : input;
}
