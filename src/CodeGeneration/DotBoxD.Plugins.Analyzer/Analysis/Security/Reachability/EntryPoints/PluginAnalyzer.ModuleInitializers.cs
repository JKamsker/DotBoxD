using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string ModuleInitializerAttribute = "System.Runtime.CompilerServices.ModuleInitializerAttribute";

    private static bool IsForbiddenApiRoot(OperationAnalysisContext context, IMethodSymbol method)
        => IsEventKernel(method.ContainingType) ||
           (IsModuleInitializer(method) && CompilationContainsEventKernel(context.Compilation));

    internal static bool IsModuleInitializer(IMethodSymbol method)
        => HasAttribute(method, ModuleInitializerAttribute);

    internal static bool CompilationContainsEventKernel(Compilation compilation)
    {
        foreach (var symbol in compilation.GetSymbolsWithName(static _ => true, SymbolFilter.Type))
        {
            if (symbol is INamedTypeSymbol type && IsEventKernel(type))
            {
                return true;
            }
        }

        return false;
    }
}
