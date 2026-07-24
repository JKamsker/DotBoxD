using System.Reflection.Emit;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class CompiledStructuralTypeEmitterTests
{
    [Fact]
    public async Task Direct_builtin_structural_inputs_and_returns_use_cached_factories_and_verify()
    {
        var listCalls = await CompileRuntimeCallsAsync(
            """{ "name": "List", "arguments": ["I32"] }""");
        var mapCalls = await CompileRuntimeCallsAsync(
            """{ "name": "Map", "arguments": ["String", "I32"] }""");
        var oneFieldRecordCalls = await CompileRuntimeCallsAsync(
            """{ "name": "Record", "arguments": ["I32"] }""");
        var twoFieldRecordCalls = await CompileRuntimeCallsAsync(
            """{ "name": "Record", "arguments": ["I32", "String"] }""");

        AssertCalls(listCalls, [nameof(CompiledRuntime.TypeListCached)]);
        AssertCalls(mapCalls, [nameof(CompiledRuntime.TypeMapCached)]);
        AssertCalls(oneFieldRecordCalls, [nameof(CompiledRuntime.TypeRecordCached)]);
        AssertCalls(twoFieldRecordCalls, [nameof(CompiledRuntime.TypeRecordCached)]);
    }

    [Fact]
    public async Task Nested_eligible_structural_nodes_use_cached_factories_and_verify()
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
        var opaqueKeyMapCalls = await CompileRuntimeCallsAsync(
            """{ "name": "Map", "arguments": ["MonsterId", "I32"] }""",
            declareOpaqueId: true);
        var recordMapCalls = await CompileRuntimeCallsAsync(
            """
            {
              "name": "Map",
              "arguments": ["String", { "name": "Record", "arguments": ["I32"] }]
            }
            """);
        var arityThreeRecordCalls = await CompileRuntimeCallsAsync(
            """{ "name": "Record", "arguments": ["I32", "String", "Bool"] }""");
        var opaqueRecordCalls = await CompileRuntimeCallsAsync(
            """{ "name": "Record", "arguments": ["I32", "MonsterId"] }""",
            declareOpaqueId: true);
        var nestedFieldRecordCalls = await CompileRuntimeCallsAsync(
            """
            {
              "name": "Record",
              "arguments": ["I32", { "name": "List", "arguments": ["I32"] }]
            }
            """);

        AssertCalls(
            nestedCalls,
            [nameof(CompiledRuntime.TypeListCached), nameof(CompiledRuntime.TypeList)]);
        AssertCalls(opaqueCalls, [nameof(CompiledRuntime.TypeList)]);
        AssertCalls(
            recordCalls,
            [nameof(CompiledRuntime.TypeRecordCached), nameof(CompiledRuntime.TypeList)]);
        AssertCalls(
            nestedMapCalls,
            [nameof(CompiledRuntime.TypeListCached), nameof(CompiledRuntime.TypeMap)]);
        AssertCalls(opaqueMapCalls, [nameof(CompiledRuntime.TypeMap)]);
        AssertCalls(opaqueKeyMapCalls, [nameof(CompiledRuntime.TypeMap)]);
        AssertCalls(
            recordMapCalls,
            [nameof(CompiledRuntime.TypeRecordCached), nameof(CompiledRuntime.TypeMap)]);
        AssertCalls(arityThreeRecordCalls, [nameof(CompiledRuntime.TypeRecord)]);
        AssertCalls(opaqueRecordCalls, [nameof(CompiledRuntime.TypeRecord)]);
        AssertCalls(
            nestedFieldRecordCalls,
            [nameof(CompiledRuntime.TypeListCached), nameof(CompiledRuntime.TypeRecord)]);
    }

    [Fact]
    public void Legacy_type_emitter_keeps_nested_structural_factories_uncached()
    {
        var nestedType = SandboxType.List(SandboxType.List(SandboxType.I32));
        var method = new DynamicMethod(
            "CreateLegacyNestedType",
            typeof(SandboxType),
            Type.EmptyTypes,
            typeof(CompiledStructuralTypeEmitterTests).Module,
            skipVisibility: true);
        var il = method.GetILGenerator();
        IlEmitterPrimitives.EmitSandboxType(il, nestedType);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        var factory = method.CreateDelegate<Func<SandboxType>>();

        var first = factory();
        var second = factory();

        Assert.Equal(nestedType, first);
        Assert.Equal(nestedType, second);
        Assert.NotSame(first, second);
        Assert.NotSame(first.Arguments[0], second.Arguments[0]);
    }

    private static async Task<RuntimeCalls> CompileRuntimeCallsAsync(
        string structuralType,
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
              "parameters": [{ "name": "value", "type": {{structuralType}} }],
              "returnType": {{structuralType}},
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
        return new RuntimeCalls(
            CallsFrom(assembly, "Execute"),
            CallsFrom(assembly, "Fn_0"));
    }

    private static IReadOnlyList<string> CallsFrom(AssemblyDefinition assembly, string methodName)
        => assembly.MainModule.Types
            .SelectMany(type => type.Methods)
            .Single(method => method.Name == methodName)
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
                nameof(CompiledRuntime.TypeRecord) or
                nameof(CompiledRuntime.TypeRecordCached))
            .Select(method => method.Name)
            .ToArray();

    private static void AssertCalls(RuntimeCalls actual, IReadOnlyList<string> expected)
    {
        Assert.Equal(expected, actual.Execute);
        Assert.Equal(expected, actual.Function);
    }

    private sealed record RuntimeCalls(
        IReadOnlyList<string> Execute,
        IReadOnlyList<string> Function);
}
