using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class CompiledExecutionEnvelopeControls
{
    private static readonly SandboxExecutionOptions AuditedOptions = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = false
    };

    private static readonly SandboxExecutionOptions FallbackOptions = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = true,
        SuppressSuccessfulRunSummaryAudit = true
    };

    public static async Task ValidateAsync(
        SandboxHost compiledHost,
        ExecutionPlan successPlan,
        ExecutionPlan failurePlan,
        CompiledExecutionInvariant successInvariant,
        SandboxPolicy policy)
    {
        await ValidateAuditedSuccessAsync(compiledHost, successPlan, successInvariant);
        await ValidateRuntimeFailureAsync(compiledHost, failurePlan);
        await ValidateVerifierFallbackAsync(policy);
    }

    private static async Task ValidateAuditedSuccessAsync(
        SandboxHost host,
        ExecutionPlan plan,
        CompiledExecutionInvariant expected)
    {
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, AuditedOptions);
        expected.ValidateCompiledEnvelope(result);
        if (result is not { Succeeded: true, Error: null, Value: I32Value { Value: 7 } } ||
            result.AuditEvents is not [
            { Kind: "RunSummary", Success: true, ErrorCode: null } summary
            ] ||
            summary.ResourceId != $"module:{plan.ModuleHash}" ||
            summary.RunId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("audited compiled success control changed");
        }
    }

    private static async Task ValidateRuntimeFailureAsync(SandboxHost host, ExecutionPlan plan)
    {
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, AuditedOptions);
        if (result is not
            {
                Succeeded: false,
                Value: null,
                Error.Code: SandboxErrorCode.InvalidInput,
                ActualMode: ExecutionMode.Compiled,
                ExecutionDispatched: true
            } ||
            string.IsNullOrWhiteSpace(result.ArtifactHash) ||
            !StringComparer.Ordinal.Equals(result.ModuleHash, plan.ModuleHash) ||
            !StringComparer.Ordinal.Equals(result.PlanHash, plan.PlanHash) ||
            !StringComparer.Ordinal.Equals(result.PolicyHash, plan.PolicyHash) ||
            result.AuditEvents is not [
            { Kind: "RunSummary", Success: false, ErrorCode: SandboxErrorCode.InvalidInput } summary
            ] ||
            summary.ResourceId != $"module:{plan.ModuleHash}" ||
            summary.RunId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("compiled runtime-failure control changed");
        }
    }

    private static async Task ValidateVerifierFallbackAsync(SandboxPolicy policy)
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new VerifierFailureCompiler());
        });
        var module = await host.ImportJsonAsync(CompiledExecutionEnvelopeModules.PureSuccess);
        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, FallbackOptions);
        if (result is not
            {
                Succeeded: true,
                Error: null,
                Value: I32Value { Value: 7 },
                ActualMode: ExecutionMode.Interpreted,
                ExecutionDispatched: true,
                ArtifactHash: null
            } ||
            result.AuditEvents is not [
            { Kind: "VerifierFailure", Success: false, ErrorCode: SandboxErrorCode.VerifierFailure } verifier,
            { Kind: "ExecutionFallback", Success: false, ErrorCode: SandboxErrorCode.VerifierFailure } fallback
            ] ||
            verifier.RunId != fallback.RunId ||
            verifier.SequenceNumber != 1 ||
            fallback.SequenceNumber != 2)
        {
            throw new InvalidOperationException("compiled verifier-fallback control changed");
        }
    }

    private sealed class VerifierFailureCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.VerifierFailure,
                "compiled artifact failed verification"));
    }
}
