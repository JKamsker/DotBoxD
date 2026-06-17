extern alias GameServerPlugin;

using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Fody;
using GameServerPlugin::DotBoxD.Kernels.Game.Plugin.Kernels;
using Mono.Cecil;

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
    public void GameServer_plugin_interceptors_do_not_use_capture_helpers()
    {
        using var module = ModuleDefinition.ReadModule(typeof(GuardianKernel).Assembly.Location);
        var generatedType = module.GetType(DotBoxDInvokeAsyncWeaverNames.GeneratedInterceptorsFullName);
        Assert.NotNull(generatedType);

        Assert.Contains(generatedType.Methods, method =>
            method.Name.StartsWith(DotBoxDInvokeAsyncWeaverNames.InvokeAsyncMethodPrefix, StringComparison.Ordinal));
        Assert.DoesNotContain(generatedType.Methods, method =>
            method.Name == DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName ||
            method.Name == DotBoxDInvokeAsyncWeaverNames.WriteCaptureMethodName);

        var moveNextMethods = generatedType.NestedTypes
            .SelectMany(static type => type.Methods)
            .Where(static method =>
                method.HasBody &&
                string.Equals(method.Name, DotBoxDInvokeAsyncWeaverNames.MoveNextMethodName, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(moveNextMethods);

        Assert.Empty(moveNextMethods.SelectMany(CaptureHelperCalls));
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

    private static bool IsCaptureHelper(MethodReference method)
        => string.Equals(method.Name, DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName, StringComparison.Ordinal) ||
           string.Equals(method.Name, DotBoxDInvokeAsyncWeaverNames.WriteCaptureMethodName, StringComparison.Ordinal);
}
