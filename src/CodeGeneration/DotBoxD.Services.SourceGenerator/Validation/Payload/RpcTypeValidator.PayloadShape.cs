using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static partial class RpcTypeValidator
{
    private static bool ContainsOpenEndedPayloadType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type.TypeKind == TypeKind.Dynamic || type.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsOpenEndedPayloadType(array.ElementType, ct);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsOpenEndedPayloadType(arg, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsRefLikeType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is INamedTypeSymbol named)
        {
            if (named.IsRefLikeType)
            {
                return true;
            }

            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsRefLikeType(arg, ct))
                {
                    return true;
                }
            }
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsRefLikeType(array.ElementType, ct);
        }

        return false;
    }
}
