using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LazyAudit;

internal static class InterpreterLazyAuditTestSupport
{
    private const string PureModuleJson = """
    {
      "id": "lazy-audit-pure",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I32" }],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "var": "value" } }]
      }]
    }
    """;

    private const string LogModuleJson = """
    {
      "id": "lazy-audit-log",
      "version": "1.0.0",
      "capabilityRequests": [{ "id": "log.write", "reason": "verify lazy audit materialization" }],
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "Unit",
        "body": [{
          "op": "return",
          "value": { "call": "log.info", "args": [{ "string": "lazy audit" }] }
        }]
      }]
    }
    """;

    public static async Task<ExecutionPlan> PreparePureAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        SandboxPolicy? policy = null)
    {
        var module = await host.ImportJsonAsync(PureModuleJson);
        return await host.PrepareAsync(
            module,
            policy ?? SandboxPolicyBuilder.Create().WithFuel(100).Build());
    }

    public static async Task<ExecutionPlan> PrepareLogAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(LogModuleJson);
        return await host.PrepareAsync(module, policy);
    }

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

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> References(params string[] ids)
        => new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["main"] = new HashSet<string>(ids, StringComparer.Ordinal)
        };

    public static SandboxExecutionOptions SuppressedOptions(
        SandboxRunId? runId = null,
        bool enableDebugTrace = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            EnableDebugTrace = enableDebugTrace,
            RunId = runId,
            SuppressSuccessfulRunSummaryAudit = true
        };
}
