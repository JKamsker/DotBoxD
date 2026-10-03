using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json;

namespace DotBoxD.Plugins.Replay;

/// <summary>Offline strict replay. Only recorded return values/errors are used; no host binding delegate is called.</summary>
public static class ExecutionReplay
{
    public static async Task<ExecutionReplayResult> RunAsync(
        ExecutionTrace trace, ExecutionMode mode = ExecutionMode.Interpreted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        Validate(trace);
        var nextCall = 0;
        string? divergence = null;
        var bindings = trace.Bindings.Select(signature => Descriptor(signature, Invoke)).ToArray();
        using var host = ExecutionRecording.CreateHost(bindings);
        var module = JsonImporter.Import(trace.ModuleJson);
        var plan = await host.PrepareAsync(module, trace.Policy.Restore(), cancellationToken).ConfigureAwait(false);
        if (plan.ModuleHash != trace.ModuleHash || plan.PolicyHash != trace.PolicyHash || plan.BindingManifestHash != trace.BindingManifestHash)
        {
            throw new InvalidDataException("Trace module, policy or binding identity does not match its recorded hash.");
        }
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (trace.InitiallyCancelled)
        {
            await runCancellation.CancelAsync().ConfigureAwait(false);
        }
        var result = await host.ExecuteAsync(plan, trace.Entrypoint, trace.Input.Restore(),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false }, runCancellation.Token).ConfigureAwait(false);
        divergence ??= nextCall == trace.Calls.Count ? null : "Execution did not consume every recorded binding call.";
        if (result.Error?.Code != trace.ErrorCode || !Equals(result.Value, trace.Result?.Restore()))
        {
            divergence ??= "Execution outcome differs from the recording.";
        }
        return new ExecutionReplayResult(divergence is null, result, divergence);

        ValueTask<SandboxValue> Invoke(SandboxContext context, string id, IReadOnlyList<SandboxValue> arguments)
        {
            if (nextCall >= trace.Calls.Count)
            {
                return Mismatch("Execution requested an unrecorded binding call.");
            }
            var call = trace.Calls[nextCall++];
            if (call.BindingId != id || !arguments.SequenceEqual(call.Arguments.Select(argument => argument.Restore())))
            {
                return Mismatch("Binding call order or arguments differ from the recording.");
            }
            foreach (var auditEvent in call.AuditEvents)
            {
                context.Audit.Write(auditEvent with { RunId = context.RunId, SequenceNumber = 0 });
            }
            if (call.Error is { } error)
            {
                throw new SandboxRuntimeException(error);
            }
            return ValueTask.FromResult(call.Result?.Restore()
                ?? throw new InvalidDataException("A successful recorded binding call requires a result."));
        }

        ValueTask<SandboxValue> Mismatch(string message)
        {
            divergence = message;
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.InvalidInput, message));
        }
    }

    private static BindingDescriptor Descriptor(BindingSignature signature,
        Func<SandboxContext, string, IReadOnlyList<SandboxValue>, ValueTask<SandboxValue>> invoke)
        => new(signature.Id, signature.Version, signature.Parameters, signature.ReturnType, signature.Effects,
            signature.RequiredCapability, signature.CostModel, signature.AuditLevel, signature.Safety,
            (context, arguments, _) => invoke(context, signature.Id, arguments), ExecutionRecording.ReplayStub)
        {
            IsAsync = signature.IsAsync,
            AuditKind = signature.AuditKind
        };

    private static void Validate(ExecutionTrace trace)
    {
        if (trace.FormatVersion != ExecutionTrace.CurrentFormatVersion || trace.IsRedacted)
        {
            throw new InvalidDataException("Unsupported or redacted trace; supply reviewed replacements before replay.");
        }
        if (trace.Calls.Any(call => !call.Completed))
        {
            throw new InvalidDataException("A binding was still running when capture ended; supply a completed recording before replay.");
        }
        if (trace.Calls.Any(call => (call.Error is null) == (call.Result is null)))
        {
            throw new InvalidDataException("Each completed boundary call must have exactly one result or error.");
        }
        ValidateLimits(trace);
    }

    private static void ValidateLimits(ExecutionTrace trace)
    {
        // A trace is untrusted input, never authority to lift local execution limits.
        var limits = trace.Policy.ResourceLimits;
        if (trace.ModuleJson.Length > ExecutionTrace.MaximumJsonLength || trace.Calls.Count > 100_000 ||
            trace.Bindings.Count > 10_000 || limits.MaxFuel > 10_000_000 || limits.MaxCallDepth > 64 ||
            limits.MaxAllocatedBytes > 64 * 1024 * 1024 || limits.EffectiveWallTime > TimeSpan.FromSeconds(30))
        {
            throw new InvalidDataException("Trace exceeds local replay safety limits.");
        }
    }
}
