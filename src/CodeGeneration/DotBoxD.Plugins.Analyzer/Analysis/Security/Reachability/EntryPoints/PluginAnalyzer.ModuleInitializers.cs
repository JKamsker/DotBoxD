using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string ModuleInitializerAttribute = "System.Runtime.CompilerServices.ModuleInitializerAttribute";

    private static bool IsForbiddenApiRoot(OperationAnalysisContext context, IMethodSymbol method)
        => IsForbiddenApiRoot(context.Compilation, method);

    private static bool IsForbiddenApiRoot(Compilation compilation, IMethodSymbol method)
        => IsEventKernel(method.ContainingType) ||
           (IsModuleInitializer(method) && CompilationContainsEventKernel(compilation));

    internal static bool IsModuleInitializer(IMethodSymbol method)
        => HasAttribute(method, ModuleInitializerAttribute);

    internal static bool CompilationContainsEventKernel(Compilation compilation)
    {
        return NamespaceContainsEventKernel(compilation.Assembly.GlobalNamespace);
    }

    private static bool NamespaceContainsEventKernel(INamespaceSymbol @namespace)
    {
        foreach (var member in @namespace.GetMembers())
        {
            if (member is INamespaceSymbol nestedNamespace && NamespaceContainsEventKernel(nestedNamespace))
            {
                return true;
            }

            if (member is INamedTypeSymbol type && TypeContainsEventKernel(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TypeContainsEventKernel(INamedTypeSymbol type)
    {
        if (IsEventKernel(type))
        {
            return true;
        }

        return type.GetTypeMembers().Any(TypeContainsEventKernel);
    }
}
