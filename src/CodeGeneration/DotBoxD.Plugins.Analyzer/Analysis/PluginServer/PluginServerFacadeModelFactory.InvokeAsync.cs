using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static bool UserDefinesPublicInvokeAsync(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers("InvokeAsync"))
        {
            if (member is IMethodSymbol
                {
                    IsStatic: false,
                    IsImplicitlyDeclared: false,
                    DeclaredAccessibility: Accessibility.Public,
                    Parameters.Length: > 0
                } method &&
                method.Parameters[0].Type.TypeKind == TypeKind.Delegate)
            {
                return true;
            }
        }

        return false;
    }
}
