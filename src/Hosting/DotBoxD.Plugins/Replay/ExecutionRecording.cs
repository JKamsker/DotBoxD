using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json;

namespace DotBoxD.Plugins.Replay;

/// <summary>Records real executions through public binding descriptors, without changing default host execution.</summary>
public static class ExecutionRecording
{
    public static async Task<ExecutionTrace> CaptureAsync(
        SandboxModule module, SandboxPolicy policy, IEnumerable<BindingDescriptor> bindings,
        string entrypoint, SandboxValue input, ExecutionMode mode = ExecutionMode.Interpreted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var calls = new BindingTraceRecorder();
        var descriptors = bindings.Select(binding => Wrap(binding, calls)).ToArray();
        using var host = CreateHost(descriptors);
        var plan = await host.PrepareAsync(module, policy).ConfigureAwait(false);
        var initiallyCancelled = cancellationToken.IsCancellationRequested;
        var result = await host.ExecuteAsync(plan, entrypoint, input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false }, cancellationToken).ConfigureAwait(false);
        return new ExecutionTrace(ExecutionTrace.CurrentFormatVersion, TraceRuntime.Current,
            JsonExporter.Export(module), plan.ModuleHash, plan.PolicyHash, plan.BindingManifestHash,
            TracePolicy.Capture(policy), plan.Bindings.Signatures, entrypoint, TraceValue.Capture(input),
            mode, result.ActualMode, initiallyCancelled, calls.Finish(result.AuditEvents),
            result.Value is null ? null : TraceValue.Capture(result.Value), result.Error?.Code);
    }

    internal static SandboxHost CreateHost(IEnumerable<BindingDescriptor> bindings) => SandboxHost.Create(builder =>
    {
        builder.UseInterpreter().UseCompilerIfAvailable();
        foreach (var binding in bindings)
        {
            builder.AddBinding(binding);
        }
    });

    internal static CompiledBinding ReplayStub { get; } =
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding));

    private static BindingDescriptor Wrap(BindingDescriptor binding, BindingTraceRecorder calls)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding with
        {
            Compiled = ReplayStub,
            Invoke = async (context, args, token) =>
            {
                var index = calls.Begin(binding.Id, args, context.Audit.EventsWritten);
                try
                {
                    var result = await binding.Invoke(context, args, token).ConfigureAwait(false);
                    calls.Complete(index, TraceValue.Capture(result), null, token.IsCancellationRequested, context.Audit.EventsWritten);
                    return result;
                }
                catch (Exception exception)
                {
                    var error = exception switch
                    {
                        SandboxRuntimeException failure => failure.Error,
                        OperationCanceledException when context.CancellationToken.IsCancellationRequested =>
                            new SandboxError(SandboxErrorCode.Cancelled, "Recorded cancellation."),
                        OperationCanceledException when token.IsCancellationRequested =>
                            new SandboxError(SandboxErrorCode.Timeout, "Recorded binding timeout."),
                        _ => new SandboxError(SandboxErrorCode.BindingFailure, "Recorded binding failure.")
                    };
                    calls.Complete(index, null, error, token.IsCancellationRequested, context.Audit.EventsWritten);
                    throw;
                }
            }
        };
    }
}
