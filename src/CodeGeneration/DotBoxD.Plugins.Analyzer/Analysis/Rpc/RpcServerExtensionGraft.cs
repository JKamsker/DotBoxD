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
        if (receiverType.TypeKind != TypeKind.Interface || !IsRpcService(receiverType))
        {
            throw new NotSupportedException(
                $"{description} '{receiverType.ToDisplayString()}' must be a server-owned [RpcService] interface.");
        }
    }

    private static IEnumerable<string> ReceiverHandleFieldNames(INamedTypeSymbol kernelType, INamedTypeSymbol receiverType)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (INamedTypeSymbol? current = kernelType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (TryGetReceiverHandleFieldName(member, kernelType, receiverType, seen, out var fieldName))
                {
                    yield return fieldName;
                }
            }
        }
    }

    private static bool TryGetReceiverHandleFieldName(
        ISymbol member,
        INamedTypeSymbol kernelType,
        INamedTypeSymbol receiverType,
        HashSet<string> seen,
        out string fieldName)
    {
        fieldName = string.Empty;
        if (member is IFieldSymbol { IsStatic: false } field)
        {
            return TryGetReceiverHandleMemberName(field, field.Type, kernelType, receiverType, seen, out fieldName);
        }

        if (member is IPropertySymbol
            {
                IsStatic: false,
                GetMethod: not null,
                SetMethod: null
            } property)
        {
            return TryGetReceiverHandleMemberName(property, property.Type, kernelType, receiverType, seen, out fieldName);
        }

        return false;
    }

    private static bool TryGetReceiverHandleMemberName(
        ISymbol member,
        ITypeSymbol memberType,
        INamedTypeSymbol kernelType,
        INamedTypeSymbol receiverType,
        HashSet<string> seen,
        out string fieldName)
    {
        fieldName = string.Empty;
        if (!IsVisibleReceiverMember(member, kernelType) ||
            !CanStoreReceiver(memberType, receiverType) ||
            !seen.Add(member.Name))
        {
            return false;
        }

        fieldName = member.Name;
        return true;
    }

    private static bool IsVisibleReceiverMember(ISymbol member, INamedTypeSymbol kernelType)
        => SymbolEqualityComparer.Default.Equals(member.ContainingType, kernelType) ||
           member.DeclaredAccessibility != Accessibility.Private;

    private static bool CanStoreReceiver(ITypeSymbol fieldType, INamedTypeSymbol receiverType)
    {
        if (SymbolEqualityComparer.Default.Equals(fieldType, receiverType))
        {
            return true;
        }

        if (fieldType is not INamedTypeSymbol namedField)
        {
            return false;
        }

        foreach (var inherited in receiverType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(namedField, inherited))
            {
                return true;
            }
        }

        return false;
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

    private static bool IsRpcService(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (DotBoxDMetadataNames.IsRpcServiceAttribute(attribute.AttributeClass?.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }
}
