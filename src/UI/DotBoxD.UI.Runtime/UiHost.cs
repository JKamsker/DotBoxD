using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels;

namespace DotBoxD.UI.Runtime;

/// <summary>Installs data-only packages using a borrowed, host-configured sandbox and kernel policy.</summary>
public sealed class UiHost
{
    private readonly SandboxHost _kernels;
    private readonly SandboxPolicy _kernelPolicy;
    private readonly UiPolicy _policy;
    private readonly SandboxExecutionOptions _execution;

    public UiHost(SandboxHost kernels, SandboxPolicy kernelPolicy, UiPolicy? policy = null, SandboxExecutionOptions? execution = null)
    {
        _kernels = kernels ?? throw new ArgumentNullException(nameof(kernels));
        _kernelPolicy = kernelPolicy ?? throw new ArgumentNullException(nameof(kernelPolicy));
        _policy = policy ?? new UiPolicy();
        _policy.Validate();
        _execution = execution ?? new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted };
    }

    /// <summary>
    /// Takes ownership of the renderer, including on failure. All package/kernel checks and initial
    /// binding evaluation precede materialization. The connection-bound remote adapter is borrowed.
    /// </summary>
    public async ValueTask<UiSession> InstallAsync(
        UiPackage package,
        IUiRenderer renderer,
        IUiRemoteTransport? remote = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var owner = new UiRendererOwner(renderer);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            UiPackageValidator.Validate(package, _policy);
            // Enforce the same byte/canonical IR boundary for hand-written packages and wire imports.
            package = UiPackageJson.Import(UiPackageJson.Export(package, _policy), _policy);
            if (package.Events.Any(e => e.Target == UiEventTarget.Remote) && remote is null)
            {
                throw new UiValidationException("UI package requires an explicit remote transport.");
            }

            var kernels = new UiKernelRunner(_kernels, _execution);
            await kernels.PrepareAsync(package, _kernelPolicy, cancellationToken).ConfigureAwait(false);
            var state = new UiStateStore(package, _policy);
            var bindings = new UiBindings(package, _policy, kernels);
            var initial = await bindings.EvaluateAsync(state.Values, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await owner.MaterializeAsync(package, initial, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            bindings.Commit(initial);
            var session = new UiSession(package, _policy, owner, remote, state, bindings, kernels);
            if (renderer is IUiInputSource inputs)
            { session.StartInput(inputs); }
            return session;
        }
        catch
        {
            await owner.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
