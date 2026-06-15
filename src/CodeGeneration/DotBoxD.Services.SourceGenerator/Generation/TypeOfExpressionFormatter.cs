using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class TypeOfExpressionFormatter
{
    private static readonly SymbolDisplayFormat s_format =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string Format(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type.TypeKind == TypeKind.Dynamic)
        {
            return ServicesGeneratorTypeNames.GlobalObject;
        }

        if (type is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } tupleUnderlyingType })
        {
            return tupleUnderlyingType.ToDisplayString(s_format);
        }

        return type.ToDisplayString(s_format);
    }
}
