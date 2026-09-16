using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class ReturnTypeClassifier
{
    private static bool HasCodeRequirement(ISymbol symbol, CancellationToken ct)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attribute.AttributeClass is { } attributeType &&
                attributeType.ToDisplayString() is
                    "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute" or
                    "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute" &&
                IsTrustedFrameworkType(attributeType))
            {
                return true;
            }
        }

        return false;
    }
}
