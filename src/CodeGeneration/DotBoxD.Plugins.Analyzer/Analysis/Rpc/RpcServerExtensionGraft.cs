using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed record RpcServerExtensionGraft(
    INamedTypeSymbol ReceiverType,
    EquatableArray<string> ReceiverHandleFields)
{
    public bool InjectsReceiverId => ReceiverHandleFields.Count > 0;

    public static RpcServerExtensionGraft? Create(INamedTypeSymbol kernelType, INamedTypeSymbol? receiverType)
    {
        if (receiverType is null)
        {
            return null;
        }

        ValidateServerOwnedReceiver(receiverType, "Server extension graft receiver");

        var fields = HasStringIdProperty(receiverType)
            ? ReceiverHandleFieldNames(kernelType, receiverType)
            : [];
        return new RpcServerExtensionGraft(receiverType, new EquatableArray<string>(fields));
    }

    public static void ValidateServerOwnedReceiver(INamedTypeSymbol receiverType, string description)
    {
        if (receiverType.TypeKind != TypeKind.Interface || !IsDotBoxDService(receiverType))
        {
            throw new NotSupportedException(
                $"{description} '{receiverType.ToDisplayString()}' must be a server-owned [DotBoxDService] interface.");
        }
    }

    private static IEnumerable<string> ReceiverHandleFieldNames(INamedTypeSymbol kernelType, INamedTypeSymbol receiverType)
    {
        foreach (var member in kernelType.GetMembers())
        {
            if (member is IFieldSymbol field &&
                SymbolEqualityComparer.Default.Equals(field.Type, receiverType))
            {
                yield return field.Name;
            }
        }
    }

    private static bool HasStringIdProperty(INamedTypeSymbol type)
    {
        if (TypeHasStringIdProperty(type))
        {
            return true;
        }

        foreach (var inherited in type.AllInterfaces)
        {
            if (TypeHasStringIdProperty(inherited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TypeHasStringIdProperty(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers("Id"))
        {
            if (member is IPropertySymbol { Parameters.Length: 0, Type.SpecialType: SpecialType.System_String })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDotBoxDService(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                DotBoxDMetadataNames.DotBoxDServiceAttribute,
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
