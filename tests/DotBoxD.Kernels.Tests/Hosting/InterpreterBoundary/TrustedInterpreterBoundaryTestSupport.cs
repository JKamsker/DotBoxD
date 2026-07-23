using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Hosting;

internal static class TrustedInterpreterBoundaryTestSupport
{
    public const string PureSuccessModule = """
    {
      "id": "trusted-interpreter-boundary-success",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "i32": 7 } }]
      }]
    }
    """;

    public const string PureFailureModule = """
    {
      "id": "trusted-interpreter-boundary-failure",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "op": "div", "left": { "i32": 1 }, "right": { "i32": 0 } }
        }]
      }]
    }
    """;

    public const string LoggedSuccessModule = """
    {
      "id": "trusted-interpreter-boundary-logged-success",
      "version": "1.0.0",
      "capabilityRequests": [{ "id": "log.write", "reason": "boundary test" }],
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "Unit",
        "body": [{
          "op": "return",
          "value": { "call": "log.info", "args": [{ "string": "boundary" }] }
        }]
      }]
    }
    """;

    public static SandboxHost CreateBuiltInHost(
        Action<SandboxAuditEvent>? observer = null,
        bool addLogBindings = false)
        => SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            if (addLogBindings)
            {
                builder.AddLogBindings();
            }

            if (observer is not null)
            {
                builder.ForwardAuditEventsTo(observer);
            }
        });

    public static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        SandboxPolicy? policy = null)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(
            module,
            policy ?? SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    public static SandboxExecutionOptions SuppressedOptions(
        ExecutionMode mode = ExecutionMode.Interpreted,
        SandboxRunId? runId = null,
        bool enableDebugTrace = false)
        => new()
        {
            Mode = mode,
            RunId = runId,
            EnableDebugTrace = enableDebugTrace,
            SuppressSuccessfulRunSummaryAudit = true
        };

    public static ExecutionPlan WithBindingReferences(
        ExecutionPlan source,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences)
        => new(
            source.ModuleHash,
            source.PlanHash,
            source.PlanSeal,
            source.PolicyHash,
            source.BindingManifestHash,
            source.Module,
            source.Policy,
            source.Bindings,
            source.Budget,
            source.FunctionAnalysis,
            bindingReferences);

    public static void AssertEnvelope(
        SandboxExecutionResult result,
        ExecutionPlan plan,
        ExecutionMode expectedMode = ExecutionMode.Interpreted)
    {
        Assert.Equal(expectedMode, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        Assert.Null(result.ArtifactHash);
        Assert.Equal(plan.ModuleHash, result.ModuleHash);
        Assert.Equal(plan.PlanHash, result.PlanHash);
        Assert.Equal(plan.PolicyHash, result.PolicyHash);
    }
}
