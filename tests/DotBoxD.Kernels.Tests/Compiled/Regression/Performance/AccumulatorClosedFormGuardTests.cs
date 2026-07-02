using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class AccumulatorClosedFormGuardTests
{
    [Fact]
    public async Task Modulo_branch_accumulator_rejects_loop_local_target()
    {
        var interpreted = await RunAsync(ModuloBranchLoopLocalTargetModuleJson(), ExecutionMode.Interpreted, 3);
        var compiled = await RunAsync(ModuloBranchLoopLocalTargetModuleJson(), ExecutionMode.Compiled, 3);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(12, ((I32Value)compiled.Value!).Value);
        Assert.Equal(interpreted.Value, compiled.Value);

        var instructions = await CompileInstructionsAsync(ModuloBranchLoopLocalTargetModuleJson());
        Assert.DoesNotContain(instructions, instruction => CallsRuntime(
            instruction,
            nameof(CompiledRuntime.AddModuloBranchDeltasI32LoopRaw)));
    }

    [Fact]
    public async Task Modulo_index_accumulator_rejects_index_target()
    {
        var interpreted = await RunAsync(ModuloIndexTargetModuleJson(), ExecutionMode.Interpreted, 5);
        var compiled = await RunAsync(ModuloIndexTargetModuleJson(), ExecutionMode.Compiled, 5);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(7, ((I32Value)compiled.Value!).Value);
        Assert.Equal(interpreted.Value, compiled.Value);

        var instructions = await CompileInstructionsAsync(ModuloIndexTargetModuleJson());
        Assert.DoesNotContain(instructions, instruction => CallsRuntime(
            instruction,
            nameof(CompiledRuntime.AddModuloIndexAccumulatorI32LoopRaw)));
    }

    [Fact]
    public void Modulo_branch_add_helper_rejects_mixed_delta_domain_without_charging()
    {
        var context = Context();

        var error = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.AddModuloBranchDeltasI32LoopRaw(
                context,
                current: int.MaxValue,
                iterations: 2,
                divisor: 2,
                match: 0,
                thenDelta: 1,
                elseDelta: -1,
                loopFuel: 11,
                thenFuel: 4,
                elseFuel: 4));

        Assert.Equal(SandboxErrorCode.InvalidInput, error.Error.Code);
        Assert.Equal(0, context.Budget.LoopIterations);
        Assert.Equal(0, context.Budget.FuelUsed);
    }

    [Theory]
    [InlineData(-1, 0, 1, 5)]
    [InlineData(5, 0, 1, 5)]
    [InlineData(0, -1, 1, 5)]
    [InlineData(0, 2_147_483_646, 2_147_483_647, 10)]
    public void Modulo_index_add_helper_rejects_unsafe_domain_without_charging(
        int current,
        int index,
        int end,
        int divisor)
    {
        var context = Context();

        var error = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.AddModuloIndexAccumulatorI32LoopRaw(
                context,
                current,
                index,
                end,
                divisor,
                conditionFuel: 3,
                loopFuel: 15));

        Assert.Equal(SandboxErrorCode.InvalidInput, error.Error.Code);
        Assert.Equal(0, context.Budget.LoopIterations);
        Assert.Equal(0, context.Budget.FuelUsed);
    }

    private static async Task<SandboxExecutionResult> RunAsync(
        string moduleJson,
        ExecutionMode mode,
        int iterations)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(20_000)
                .WithMaxLoopIterations(1_000)
                .Build());

        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static async Task<IReadOnlyList<Instruction>> CompileInstructionsAsync(string moduleJson)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(20_000).Build());
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());
        var artifact = await compiler.CompileAsync(plan, new CompileOptions("main"), CancellationToken.None);
        using var image = new MemoryStream(artifact.AssemblyBytes);
        using var assembly = AssemblyDefinition.ReadAssembly(image);
        return assembly.MainModule.Types
            .SelectMany(type => type.Methods)
            .Single(method => method.Name == "Fn_0")
            .Body
            .Instructions
            .ToArray();
    }

    private static SandboxContext Context()
    {
        var limits = new ResourceLimits(MaxFuel: 1_000_000, MaxLoopIterations: 1_000_000);
        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private static string ModuloBranchLoopLocalTargetModuleJson()
        => """
        {
          "id": "modulo-branch-loop-local-target",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "i", "value": { "i32": 0 } },
              { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                "body": [{ "op": "if",
                  "condition": { "op": "eq", "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } }, "right": { "i32": 0 } },
                  "then": [{ "op": "set", "name": "i", "value": { "op": "add", "left": { "var": "i" }, "right": { "i32": 10 } } }],
                  "else": [{ "op": "set", "name": "i", "value": { "op": "add", "left": { "var": "i" }, "right": { "i32": 20 } } }]
                }] },
              { "op": "return", "value": { "var": "i" } }
            ]
          }]
        }
        """;

    private static string ModuloIndexTargetModuleJson()
        => """
        {
          "id": "modulo-index-target",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "i", "value": { "i32": 0 } },
              { "op": "while", "condition": { "op": "lt", "left": { "var": "i" }, "right": { "var": "iterations" } },
                "body": [
                  { "op": "set", "name": "i", "value": {
                    "op": "rem",
                    "left": { "op": "add", "left": { "var": "i" }, "right": { "var": "i" } },
                    "right": { "i32": 100 } } },
                  { "op": "set", "name": "i", "value": { "op": "add", "left": { "var": "i" }, "right": { "i32": 1 } } }
                ] },
              { "op": "return", "value": { "var": "i" } }
            ]
          }]
        }
        """;

    private static bool CallsRuntime(Instruction instruction, string method)
        => instruction.OpCode.Code == Code.Call &&
           instruction.Operand is MethodReference
           {
               Name: var name,
               DeclaringType.FullName: "DotBoxD.Kernels.Runtime.CompiledRuntime"
           } &&
           string.Equals(name, method, StringComparison.Ordinal);
}
