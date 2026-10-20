using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.UI;

/// <summary>Rejects RPC receiver conventions that local UI kernels cannot preserve.</summary>
internal static class UiInvocationValidator
{
    public static void Validate(IMethodSymbol method, Compilation compilation)
    {
        if (DotBoxDRpcJsonLowerer.HasRpcServiceAttribute(method.ReturnType))
        {
            throw new NotSupportedException(
                "Local UI kernels cannot create scoped RPC service handles; capture the scalar key and pass it to a static host binding or use a remote handler.");
        }

        if (DotBoxDHostBindingExpressionLowerer.HostBinding(method, compilation) is not null &&
            (!method.IsStatic || method.ReducedFrom is not null ||
             DotBoxDHostBindingExpressionLowerer.IncludesValueReceiver(method, compilation)))
        {
            throw new NotSupportedException(
                "Local UI host bindings require static methods with explicit value arguments; pass receiver data to a static host binding or use a remote handler.");
        }
    }
}
