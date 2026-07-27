using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class CompiledReturnValidationEmissionTests
{
    [Fact]
    public async Task Selected_structural_entrypoint_records_each_return_but_reachable_helper_only_validates()
    {
        var artifact = await CompileAsync(StructuralEntrypointWithHelperJson(), "main");

        Assert.True(artifact.Verification.Succeeded);
        using var image = new MemoryStream(artifact.AssemblyBytes);
        using var assembly = AssemblyDefinition.ReadAssembly(image);

        Assert.Equal(
            [
                nameof(CompiledRuntime.RequireValueTypeAndRecordValidation),
                nameof(CompiledRuntime.RequireValueTypeAndRecordValidation)
            ],
            ReturnValidationCalls(assembly, "Fn_0"));
        Assert.Equal(
            [nameof(CompiledRuntime.RequireValueType)],
            ReturnValidationCalls(assembly, "Fn_1"));
        Assert.Empty(ReturnValidationCalls(assembly, "Execute"));
    }

    [Theory]
    [InlineData("I32", "{ \"i32\": 7 }")]
    [InlineData("String", "{ \"string\": \"value\" }")]
    public async Task Selected_scalar_entrypoint_keeps_regular_return_validation(
        string returnType,
        string value)
    {
        var artifact = await CompileAsync(ScalarEntrypointJson(returnType, value), "main");

        Assert.True(artifact.Verification.Succeeded);
        using var image = new MemoryStream(artifact.AssemblyBytes);
        using var assembly = AssemblyDefinition.ReadAssembly(image);

        Assert.Equal(
            [nameof(CompiledRuntime.RequireValueType)],
            ReturnValidationCalls(assembly, "Fn_0"));
    }

    private static async Task<CompiledArtifact> CompileAsync(string moduleJson, string entrypoint)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());
        return await compiler.CompileAsync(
            plan,
            new CompileOptions(entrypoint),
            CancellationToken.None);
    }

    private static IReadOnlyList<string> ReturnValidationCalls(
        AssemblyDefinition assembly,
        string methodName)
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
                nameof(CompiledRuntime.RequireValueType) or
                nameof(CompiledRuntime.RequireValueTypeAndRecordValidation))
            .Select(method => method.Name)
            .ToArray();

    private static string StructuralEntrypointWithHelperJson()
        => """
        {
          "id": "compiled-return-validation-emission",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "condition", "type": "Bool" },
                {
                  "name": "values",
                  "type": { "name": "List", "arguments": ["I32"] }
                }
              ],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [
                {
                  "op": "if",
                  "condition": { "var": "condition" },
                  "then": [
                    {
                      "op": "return",
                      "value": { "call": "helper", "args": [{ "var": "values" }] }
                    }
                  ],
                  "else": [{ "op": "return", "value": { "var": "values" } }]
                }
              ]
            },
            {
              "id": "helper",
              "visibility": "private",
              "parameters": [
                {
                  "name": "values",
                  "type": { "name": "List", "arguments": ["I32"] }
                }
              ],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [{ "op": "return", "value": { "var": "values" } }]
            }
          ]
        }
        """;

    private static string ScalarEntrypointJson(string returnType, string value)
        => $$"""
        {
          "id": "compiled-scalar-return-validation-emission",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{value}} }]
            }
          ]
        }
        """;
}
