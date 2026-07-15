using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I64LoopPlanSemanticsTests
{
    [Fact]
    public async Task Later_assignment_target_is_not_readable_by_an_earlier_plan_expression()
    {
        using var host = SandboxTestHost.Create();
        var prepared = await PrepareAsync(host, AssignedControlModule);
        var unassignedModule = await host.ImportJsonAsync(ReadBeforeAssignmentModule);
        var tampered = ReplaceModule(prepared, unassignedModule);

        var result = await new SandboxInterpreter().ExecuteAsync(
            tampered,
            "main",
            SandboxValue.Unit,
            Options(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal("local 'later' read before assignment", result.Error.SafeMessage);
    }

    [Fact]
    public async Task Planned_i64_arithmetic_remains_checked()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, CheckedArithmeticModule);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt64(long.MaxValue),
            Options());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal("integer overflow", result.Error.SafeMessage);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, Policy());
    }

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithMaxLoopIterations(10)
            .Build();

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private static ExecutionPlan ReplaceModule(ExecutionPlan plan, SandboxModule module)
        => new(
            plan.ModuleHash,
            plan.PlanHash,
            plan.PlanSeal,
            plan.PolicyHash,
            plan.BindingManifestHash,
            module,
            plan.Policy,
            plan.Bindings,
            plan.Budget,
            plan.FunctionAnalysis,
            plan.BindingReferences);

    private const string AssignedControlModule = """
    {
      "id": "i64-loop-plan-assigned-control",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "later", "value": { "i64": 5 } },
          { "op": "set", "name": "total", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 1 },
            "body": [
              {
                "op": "set",
                "name": "total",
                "value": { "op": "add", "left": { "var": "later" }, "right": { "i64": 1 } }
              },
              { "op": "set", "name": "later", "value": { "i64": 5 } }
            ]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    private const string ReadBeforeAssignmentModule = """
    {
      "id": "i64-loop-plan-read-before-assignment",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "total", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 1 },
            "body": [
              {
                "op": "set",
                "name": "total",
                "value": { "op": "add", "left": { "var": "later" }, "right": { "i64": 1 } }
              },
              { "op": "set", "name": "later", "value": { "i64": 5 } }
            ]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    private const string CheckedArithmeticModule = """
    {
      "id": "i64-loop-plan-checked-arithmetic",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I64" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "result", "value": { "var": "value" } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 1 },
            "body": [{
              "op": "set",
              "name": "result",
              "value": { "op": "add", "left": { "var": "result" }, "right": { "i64": 1 } }
            }]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;
}
