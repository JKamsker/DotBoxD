extern alias GameServerPlugin;

using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Fody;
using GameServerPlugin::DotBoxD.Kernels.Game.Plugin.Kernels;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Weaving;

public sealed class InvokeAsyncImplicitCaptureWeaverTests
{
    [Fact]
    public void Weaver_name_constants_match_generator_contracts()
    {
        Assert.Equal(
            DotBoxDGenerationNames.TypeNames.GeneratedInterceptorsNamespace,
            DotBoxDInvokeAsyncWeaverNames.GeneratedInterceptorsNamespace);
        Assert.Equal("InvokeAsyncInterceptors", DotBoxDInvokeAsyncWeaverNames.GeneratedInterceptorsTypeName);
        Assert.Equal("InvokeAsync_", DotBoxDInvokeAsyncWeaverNames.InvokeAsyncMethodPrefix);
        Assert.Equal("__ReadCapture", DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName);
        Assert.Equal("__WriteCapture", DotBoxDInvokeAsyncWeaverNames.WriteCaptureMethodName);
        Assert.Equal("lambda", DotBoxDInvokeAsyncWeaverNames.LambdaParameterName);
    }

    [Fact]
    public void GameServer_plugin_implicit_capture_interceptor_uses_weaved_field_access()
    {
        using var module = ModuleDefinition.ReadModule(typeof(GuardianKernel).Assembly.Location);
        var generatedType = module.GetType(DotBoxDInvokeAsyncWeaverNames.GeneratedInterceptorsFullName);
        Assert.NotNull(generatedType);

        Assert.Contains(generatedType.Methods, method =>
            method.Name == DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName);
        Assert.Contains(generatedType.Methods, method =>
            method.Name == DotBoxDInvokeAsyncWeaverNames.WriteCaptureMethodName);

        var moveNextMethods = generatedType.NestedTypes
            .SelectMany(static type => type.Methods)
            .Where(static method =>
                method.HasBody &&
                string.Equals(method.Name, DotBoxDInvokeAsyncWeaverNames.MoveNextMethodName, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(moveNextMethods);

        Assert.Empty(moveNextMethods.SelectMany(CaptureHelperCalls));
        var fieldAccesses = moveNextMethods.SelectMany(CaptureFieldAccesses).ToArray();
        Assert.Contains(fieldAccesses, access =>
            access.Code == Code.Ldfld && access.Field.Name == "implicitMonsterId");
        Assert.Contains(fieldAccesses, access =>
            access.Code == Code.Stfld && access.Field.Name == "implicitLastHealth");

        var closureType = module.GetType("DotBoxD.Kernels.Game.Plugin.Program")!
            .NestedTypes
            .Single(type => type.Fields.Any(field => field.Name == "implicitMonsterId"));
        Assert.True(closureType.IsNestedAssembly);
    }

    private static IEnumerable<MethodReference> CaptureHelperCalls(MethodDefinition method)
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand is MethodReference candidate &&
                IsCaptureHelper(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<(Code Code, FieldReference Field)> CaptureFieldAccesses(MethodDefinition method)
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction is { Operand: FieldReference field } &&
                field.DeclaringType.FullName.Contains("<>c__DisplayClass", StringComparison.Ordinal))
            {
                yield return (instruction.OpCode.Code, field);
            }
        }
    }

    private static bool IsCaptureHelper(MethodReference method)
        => string.Equals(method.Name, DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName, StringComparison.Ordinal) ||
           string.Equals(method.Name, DotBoxDInvokeAsyncWeaverNames.WriteCaptureMethodName, StringComparison.Ordinal);
}
