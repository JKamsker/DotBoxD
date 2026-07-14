using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class CompiledStructuralTypeEmitterTests
{
    [Fact]
    public async Task Direct_builtin_structural_returns_use_cached_factories_and_verify()
    {
        var listCalls = await CompileRuntimeCallsAsync(
            """{ "name": "List", "arguments": ["I32"] }""");
        var mapCalls = await CompileRuntimeCallsAsync(
            """{ "name": "Map", "arguments": ["String", "I32"] }""");

        Assert.Equal([nameof(CompiledRuntime.TypeListCached)], listCalls);
        Assert.Equal([nameof(CompiledRuntime.TypeMapCached)], mapCalls);
    }

    [Fact]
    public async Task Non_direct_builtin_structural_returns_use_legacy_factories_and_verify()
    {
        var nestedCalls = await CompileRuntimeCallsAsync(
            """
            {
              "name": "List",
              "arguments": [{ "name": "List", "arguments": ["I32"] }]
            }
            """);
        var opaqueCalls = await CompileRuntimeCallsAsync(
            """{ "name": "List", "arguments": ["MonsterId"] }""",
            declareOpaqueId: true);
        var recordCalls = await CompileRuntimeCallsAsync(
            """
            {
              "name": "List",
              "arguments": [{ "name": "Record", "arguments": ["I32"] }]
            }
            """);
        var nestedMapCalls = await CompileRuntimeCallsAsync(
            """
            {
              "name": "Map",
              "arguments": ["String", { "name": "List", "arguments": ["I32"] }]
            }
            """);
        var opaqueMapCalls = await CompileRuntimeCallsAsync(
            """{ "name": "Map", "arguments": ["String", "MonsterId"] }""",
            declareOpaqueId: true);
        var recordMapCalls = await CompileRuntimeCallsAsync(
            """
            {
              "name": "Map",
              "arguments": ["String", { "name": "Record", "arguments": ["I32"] }]
            }
            """);

        Assert.Equal(
            [nameof(CompiledRuntime.TypeList), nameof(CompiledRuntime.TypeList)],
            nestedCalls);
        Assert.Equal([nameof(CompiledRuntime.TypeList)], opaqueCalls);
        Assert.Equal(
            [nameof(CompiledRuntime.TypeRecord), nameof(CompiledRuntime.TypeList)],
            recordCalls);
        Assert.Equal(
            [nameof(CompiledRuntime.TypeList), nameof(CompiledRuntime.TypeMap)],
            nestedMapCalls);
        Assert.Equal([nameof(CompiledRuntime.TypeMap)], opaqueMapCalls);
        Assert.Equal(
            [nameof(CompiledRuntime.TypeRecord), nameof(CompiledRuntime.TypeMap)],
            recordMapCalls);
    }

    private static async Task<IReadOnlyList<string>> CompileRuntimeCallsAsync(
        string returnType,
        bool declareOpaqueId = false)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "compiled-structural-type-factory-selection",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "type": {{returnType}} }],
              "returnType": {{returnType}},
              "body": [{ "op": "return", "value": { "var": "value" } }]
            }
          ]
        }
        """);
        var policyBuilder = SandboxPolicyBuilder.Create().WithFuel(1_000);
        if (declareOpaqueId)
        {
            policyBuilder.DeclareOpaqueIdType("MonsterId");
        }

        var plan = await host.PrepareAsync(module, policyBuilder.Build());
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());
        var artifact = await compiler.CompileAsync(plan, new CompileOptions("main"), CancellationToken.None);

        Assert.True(artifact.Verification.Succeeded);
        using var image = new MemoryStream(artifact.AssemblyBytes);
        using var assembly = AssemblyDefinition.ReadAssembly(image);
        return assembly.MainModule.Types
            .SelectMany(type => type.Methods)
            .Single(method => method.Name == "Fn_0")
            .Body
            .Instructions
            .Where(instruction => instruction.OpCode.Code == Code.Call)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Where(method => method.DeclaringType.FullName == typeof(CompiledRuntime).FullName)
            .Where(method => method.Name is
                nameof(CompiledRuntime.TypeList) or
                nameof(CompiledRuntime.TypeListCached) or
                nameof(CompiledRuntime.TypeMap) or
                nameof(CompiledRuntime.TypeMapCached) or
                nameof(CompiledRuntime.TypeRecord))
            .Select(method => method.Name)
            .ToArray();
    }
}
