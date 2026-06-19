using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

/// <summary>
/// Resolves the reverse server-&gt;plugin event-callback contract for a generated plugin facade, discovered by
/// the same <c>{worldNs}.Ipc</c> convention the factory uses for the control service. Optional: a world with no
/// such contract keeps the original facade (no local handlers). A shape guard requires the
/// <c>[DotBoxDService]</c> interface to carry the expected
/// <c>OnEventAsync(string, byte[], CancellationToken) -&gt; ValueTask</c> method so the generated sink compiles;
/// a mismatching type is ignored rather than emitting broken code.
/// </summary>
internal static class PluginServerEventCallbackResolver
{
    public static INamedTypeSymbol? Resolve(Compilation compilation, INamedTypeSymbol worldType)
    {
        var worldNamespace = worldType.ContainingNamespace.ToDisplayString();
        var callback = compilation.GetTypeByMetadataName(worldNamespace + ".Ipc.IPluginEventCallback");
        if (callback is null ||
            callback.TypeKind != TypeKind.Interface ||
            !HasDotBoxDServiceAttribute(callback))
        {
            return null;
        }

        return HasEventCallbackShape(callback) ? callback : null;
    }

    private static bool HasEventCallbackShape(INamedTypeSymbol callback)
    {
        foreach (var member in callback.GetMembers("OnEventAsync"))
        {
            if (member is IMethodSymbol { Parameters.Length: 3 } method &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                method.Parameters[1].Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } &&
                string.Equals(
                    method.Parameters[2].Type.ToDisplayString(),
                    "System.Threading.CancellationToken",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDGenerationNames.Metadata.DotBoxDServiceAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
