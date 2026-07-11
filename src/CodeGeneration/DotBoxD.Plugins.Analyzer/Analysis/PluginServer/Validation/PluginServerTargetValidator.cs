using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerTargetValidator
{
    public static void Validate(INamedTypeSymbol serverType, CancellationToken cancellationToken)
    {
        if (serverType.TypeKind != TypeKind.Class)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be a class.");
        }

        if (serverType.IsGenericType)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be non-generic.");
        }

        if (serverType.ContainingType is not null)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be non-nested.");
        }

        if (serverType.IsAbstract)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be concrete.");
        }

        if (!PluginServerFacadeModelFactory.IsPartialClass(serverType, cancellationToken))
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be partial.");
        }
    }
}
