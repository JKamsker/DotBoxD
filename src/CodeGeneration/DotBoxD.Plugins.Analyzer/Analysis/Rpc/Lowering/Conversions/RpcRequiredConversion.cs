using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcRequiredConversion
{
    public static bool IsSupported(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        Compilation compilation)
        => RpcNumericConversion.IsSupported(sourceType, targetType) ||
           IsRepresentationPreservingCollectionConversion(sourceType, targetType, compilation);

    public static bool TryApply(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        Compilation compilation,
        string lowered,
        out string converted)
    {
        if (RpcNumericConversion.TryApply(sourceType, targetType, lowered, out converted))
        {
            return true;
        }

        if (IsRepresentationPreservingCollectionConversion(sourceType, targetType, compilation))
        {
            converted = lowered;
            return true;
        }

        converted = lowered;
        return false;
    }

    private static bool IsRepresentationPreservingCollectionConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            return true;
        }

        if (!IsBuiltInImplicitConversion(sourceType, targetType, compilation))
        {
            return false;
        }

        if (DotBoxDRpcTypeMapper.ListElementType(sourceType) is { } sourceElement &&
            DotBoxDRpcTypeMapper.ListElementType(targetType) is { } targetElement)
        {
            return AreRepresentationEquivalentMembers(sourceElement, targetElement, compilation);
        }

        if (DotBoxDRpcTypeMapper.MapTypes(sourceType) is { } sourceMap &&
            DotBoxDRpcTypeMapper.MapTypes(targetType) is { } targetMap)
        {
            return AreRepresentationEquivalentMembers(sourceMap.Key, targetMap.Key, compilation) &&
                   AreRepresentationEquivalentMembers(sourceMap.Value, targetMap.Value, compilation);
        }

        return false;
    }

    private static bool AreRepresentationEquivalentMembers(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        Compilation compilation)
        => SymbolEqualityComparer.Default.Equals(sourceType, targetType) ||
           IsRepresentationPreservingCollectionConversion(sourceType, targetType, compilation);

    private static bool IsBuiltInImplicitConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        Compilation compilation)
    {
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            return false;
        }

        var conversion = csharpCompilation.ClassifyConversion(sourceType, targetType);
        return conversion.Exists && conversion.IsImplicit && !conversion.IsUserDefined;
    }
}
