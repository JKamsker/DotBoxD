using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.BindingDispatch;

internal static class InterpreterScalarBindingModules
{
    internal const string Unary = """
    {
      "id": "interpreter-scalar-binding-unary",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "call": "test.unary", "args": [{ "i32": 41 }] }
        }]
      }]
    }
    """;

    internal const string Binary = """
    {
      "id": "interpreter-scalar-binding-binary",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "call": "test.binary", "args": [{ "i32": 4 }, { "i32": 2 }] }
        }]
      }]
    }
    """;

    internal const string RetainedArguments = """
    {
      "id": "interpreter-binding-retained-arguments",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "Unit",
        "body": [
          {
            "op": "expr",
            "value": { "call": "test.retain", "args": [{ "i32": 1 }, { "i32": 2 }] }
          },
          {
            "op": "return",
            "value": { "call": "test.retain", "args": [{ "i32": 3 }, { "i32": 4 }] }
          }
        ]
      }]
    }
    """;

    internal const string OrderedOperands = """
    {
      "id": "interpreter-scalar-binding-ordered-operands",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": {
            "call": "test.ordered",
            "args": [
              { "call": "test.first", "args": [] },
              { "call": "test.second", "args": [] }
            ]
          }
        }]
      }]
    }
    """;

    internal const string PendingUnary = """
    {
      "id": "interpreter-scalar-binding-pending-result",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "call": "test.pendingUnary", "args": [{ "i32": 41 }] }
        }]
      }]
    }
    """;

    internal const string LocalFunctionShadowsFastBinding = """
    {
      "id": "interpreter-local-function-shadows-fast-binding",
      "version": "1.0.0",
      "functions": [
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": "I32",
          "body": [{
            "op": "return",
            "value": { "call": "test.shadow", "args": [{ "i32": 41 }] }
          }]
        },
        {
          "id": "test.shadow",
          "parameters": [{ "name": "value", "type": "I32" }],
          "returnType": "I32",
          "body": [{ "op": "return", "value": { "i32": 7 } }]
        }
      ]
    }
    """;

    internal const string Ternary = """
    {
      "id": "interpreter-scalar-binding-ternary",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": {
            "call": "test.ternary",
            "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }]
          }
        }]
      }]
    }
    """;

    internal const string RetainedTernaryArguments = """
    {
      "id": "interpreter-binding-retained-ternary-arguments",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [
          {
            "op": "expr",
            "value": {
              "call": "test.ternary",
              "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }]
            }
          },
          {
            "op": "return",
            "value": {
              "call": "test.ternary",
              "args": [{ "i32": 4 }, { "i32": 5 }, { "i32": 6 }]
            }
          }
        ]
      }]
    }
    """;

    internal const string OrderedTernaryOperands = """
    {
      "id": "interpreter-scalar-binding-ordered-ternary-operands",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": {
            "call": "test.ordered3",
            "args": [
              { "call": "test.first", "args": [] },
              { "call": "test.second", "args": [] },
              { "call": "test.third", "args": [] }
            ]
          }
        }]
      }]
    }
    """;

    internal const string LocalFunctionShadowsFastTernaryBinding = """
    {
      "id": "interpreter-local-function-shadows-fast-ternary-binding",
      "version": "1.0.0",
      "functions": [
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": "I32",
          "body": [{
            "op": "return",
            "value": {
              "call": "test.shadow3",
              "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }]
            }
          }]
        },
        {
          "id": "test.shadow3",
          "parameters": [
            { "name": "first", "type": "I32" },
            { "name": "second", "type": "I32" },
            { "name": "third", "type": "I32" }
          ],
          "returnType": "I32",
          "body": [{ "op": "return", "value": { "i32": 7 } }]
        }
      ]
    }
    """;

    internal static SandboxHost CreateHost(params BindingDescriptor[] bindings)
        => SandboxHost.Create(builder =>
        {
            foreach (var binding in bindings)
            {
                builder.AddBinding(binding);
            }

            builder.UseInterpreter();
        });

    internal static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        bool allowAsync = false)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5));
        if (allowAsync)
        {
            policy.AllowRuntimeAsync();
        }

        return await host.PrepareAsync(module, policy.Build());
    }

    internal static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };
}
