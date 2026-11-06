using System.Collections.Generic;
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

    public static string Format(
        ITypeSymbol type,
        CancellationToken ct,
        ExternAliasTypeFormatter? aliasFormatter = null,
        List<string>? usedAliases = null)
    {
        ct.ThrowIfCancellationRequested();

        if (type.TypeKind == TypeKind.Dynamic)
        {
            return ServicesGeneratorTypeNames.GlobalObject;
        }

        if (type is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } tupleUnderlyingType })
        {
            type = tupleUnderlyingType;
        }

        return aliasFormatter is null
            ? type.ToDisplayString(s_format)
            : aliasFormatter.Format(type, s_format, usedAliases);
    }
}
