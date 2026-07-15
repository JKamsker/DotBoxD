using System.Security.Cryptography;
using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost : IDisposable
{
    private static readonly SandboxExecutionOptions DefaultExecutionOptions = new();

    private readonly BindingRegistry _bindings;
    private readonly ISandboxInterpreter _interpreter;
    private readonly CompiledExecutionProvider _compiled;
    private readonly IExecutionModeSelector _modeSelector;
    private readonly Action<SandboxAuditEvent>[] _auditObservers;
    private readonly SandboxWorkerExecutor _workerExecutor;
    private readonly byte[] _planSigningKey = RandomNumberGenerator.GetBytes(32);
    private readonly AutoExecutionHotness _autoHotness = new();
    private readonly PreparedPlanIntegrityCache _preparedPlans = new();
    private int _disposed;

    internal SandboxHost(
        BindingRegistry bindings,
        ISandboxInterpreter interpreter,
        ISandboxCompiler? compiler,
        IExecutionModeSelector modeSelector,
        Action<SandboxAuditEvent>? auditObserver,
        ConfiguredSandboxWorker? worker)
    {
        _bindings = bindings;
        _interpreter = interpreter;
        _compiled = new CompiledExecutionProvider(compiler);
        _modeSelector = modeSelector;
        _auditObservers = SnapshotAuditObservers(auditObserver);
        _workerExecutor = new SandboxWorkerExecutor(worker);
    }

    public static SandboxHost Create(Action<SandboxHostBuilder>? configure = null)
    {
        var builder = new SandboxHostBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    public ValueTask<ExecutionPlan> PrepareAsync(
        SandboxModule module,
        SandboxPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionPlanGuard.EnsurePolicyLimits(policy);
        var validation = new ModuleValidator().Validate(module, _bindings, policy);
        if (!validation.Succeeded)
        {
            throw new SandboxValidationException(validation.Diagnostics);
        }

        var plan = ExecutionPlanBuilder.Build(
            module,
            policy,
            _bindings,
            validation.Functions,
            validation.BindingReferences,
            _planSigningKey);
        _preparedPlans.Register(plan);
        return ValueTask.FromResult(plan);
    }

    internal IReadOnlyList<string> GetRequiredCapabilities(SandboxModule module)
        => GetRequiredCapabilities(module, policy: null);

    internal IReadOnlyList<string> GetRequiredCapabilities(
        SandboxModule module,
        SandboxPolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(module);
        ThrowIfDisposed();

        var declaredOpaqueIdTypes = policy?.DeclaredOpaqueIdTypes ?? new HashSet<string>(StringComparer.Ordinal);
        var validation = new ModuleValidator().ValidateForCapabilityDiscovery(
            module,
            _bindings,
            declaredOpaqueIdTypes);
        if (!validation.Succeeded)
        {
            throw new SandboxValidationException(validation.Diagnostics);
        }

        return validation.RequiredCapabilities.Order(StringComparer.Ordinal).ToArray();
    }

    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entrypoint);
        ArgumentNullException.ThrowIfNull(input);
        options ??= DefaultExecutionOptions;
        ExecutionPlanGuard.EnsurePrepared(plan, _bindings, _planSigningKey, _preparedPlans);
        if (TryGetPreDispatchResult(plan, entrypoint, options, cancellationToken, out var preDispatchResult))
        {
            return Publish(preDispatchResult);
        }

        var result = options.Isolation == SandboxIsolation.WorkerProcess
            ? await _workerExecutor.ExecuteAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false)
            : await ExecuteSelectedModeAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
        return Publish(result);
    }

    private async ValueTask<SandboxExecutionResult> ExecuteSelectedModeAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
        => options.Mode switch
        {
            ExecutionMode.Compiled => await ExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false),
            ExecutionMode.Interpreted => await ExecuteInterpretedAsync(
                    plan,
                    entrypoint,
                    input,
                    options,
                    cancellationToken)
                .ConfigureAwait(false),
            ExecutionMode.Auto => await ExecuteAutoAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false),
            _ => CompilerUnavailableResult(plan, options)
        };

    private bool TryGetPreDispatchResult(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        out SandboxExecutionResult result)
    {
        if (!Enum.IsDefined(options.Mode))
        {
            result = InvalidExecutionOptionsResult(
                plan,
                options,
                $"execution mode '{(int)options.Mode}' is not supported");
            return true;
        }

        if (!Enum.IsDefined(options.Isolation))
        {
            result = InvalidExecutionOptionsResult(
                plan,
                options,
                $"sandbox isolation '{(int)options.Isolation}' is not supported");
            return true;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            result = PreDispatchCancelledResult(plan, options);
            return true;
        }

        if (TryGetCapabilityDenial(plan, entrypoint, out var denial))
        {
            result = CapabilityDeniedResult(plan, options, denial);
            return true;
        }

        if (options.RequireDeterministic && !plan.Policy.Deterministic)
        {
            result = DeterminismRequiredResult(plan, options);
            return true;
        }

        result = null!;
        return false;
    }

    private static SandboxExecutionResult PreDispatchCancelledResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
        var audit = new InMemoryAuditSink();
        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, options.Mode, error, false);
        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.OwnedEventSnapshot(),
            ActualMode = options.Mode,
            ExecutionDispatched = false,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private SandboxExecutionResult Publish(SandboxExecutionResult result)
    {
        if (_auditObservers.Length == 0)
        {
            return result;
        }

        foreach (var auditEvent in result.AuditEvents)
        {
            PublishToAuditObservers(auditEvent);
        }

        return result;
    }

    private void PublishToAuditObservers(SandboxAuditEvent auditEvent)
    {
        // The observer set is fixed for the lifetime of the host, so dispatch reuses the
        // snapshot captured at construction instead of materializing the multicast invocation
        // list per audit event.
        foreach (var observer in _auditObservers)
        {
            try
            {
                observer(auditEvent);
            }
#pragma warning disable RCS1075 // Intentional isolation boundary: audit-observer failures must never alter sandbox execution.
            catch (Exception)
            {
                // Operational forwarding failures must not change sandbox execution behavior.
            }
#pragma warning restore RCS1075
        }
    }

    private static Action<SandboxAuditEvent>[] SnapshotAuditObservers(Action<SandboxAuditEvent>? auditObserver)
    {
        if (auditObserver is null)
        {
            return [];
        }

        var observers = auditObserver.GetInvocationList();
        var snapshot = new Action<SandboxAuditEvent>[observers.Length];
        for (var i = 0; i < observers.Length; i++)
        {
            snapshot[i] = (Action<SandboxAuditEvent>)observers[i];
        }

        return snapshot;
    }

    private async ValueTask<SandboxExecutionResult> ExecuteInterpretedAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
        => await InterpreterExecutionBoundary.ExecuteAsync(
                _interpreter,
                plan,
                entrypoint,
                input,
                options,
                cancellationToken)
            .ConfigureAwait(false);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _compiled.Dispose();
        }
    }

    internal void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
