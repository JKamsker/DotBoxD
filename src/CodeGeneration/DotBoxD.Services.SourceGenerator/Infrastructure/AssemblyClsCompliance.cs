using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Infrastructure;

internal static class AssemblyClsCompliance
{
    public static bool IsEnabled(Compilation compilation)
    {
        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
                "global::System.CLSCompliantAttribute")
            {
                continue;
            }

            return attr.ConstructorArguments.Length == 1 &&
                attr.ConstructorArguments[0].Value is true;
        }

        return false;
    }
}
