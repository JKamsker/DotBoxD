using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class SubServiceReturnTypeReader
{
    private const string SystemThreadingTasks = ServicesGeneratorTypeNames.SystemThreadingTasksNamespace;
    private static readonly SymbolDisplayFormat s_qualifiedIdentityFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static bool TryGetAsyncPayloadType(ITypeSymbol type, out ITypeSymbol payloadType)
    {
        payloadType = null!;
        if (type is not INamedTypeSymbol named || !named.IsGenericType)
        {
            return false;
        }

        var nsName = named.ContainingNamespace?.ToDisplayString();
        if (nsName != SystemThreadingTasks ||
            (named.Name != "Task" && named.Name != "ValueTask"))
        {
            return false;
        }

        payloadType = named.TypeArguments[0];
        return true;
    }

    public static bool IsRpcServiceInterface(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        foreach (var attr in named.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (ServicesGeneratorTypeNames.IsRpcServiceAttribute(attr.AttributeClass))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsRpcInvokerType(ITypeSymbol type)
        => type.ToDisplayString(s_qualifiedIdentityFormat) == ServicesGeneratorTypeNames.GlobalRpcInvoker;

    public static bool TryGetSubServiceInfo(ITypeSymbol type, CancellationToken ct, out SubServiceInfo info)
    {
        ct.ThrowIfCancellationRequested();

        info = null!;
        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        if (named.IsGenericType || named.ContainingType is not null)
        {
            return false;
        }

        if (!TryGetDotBoxDServiceAttribute(named, ct, out var serviceAttr))
        {
            return false;
        }

        info = new SubServiceInfo(
            QualifiedInterfaceName: named.ToDisplayString(s_qualifiedIdentityFormat),
            ServiceName: LiteralHelpers.EscapeStringLiteral(ReadServiceName(named, serviceAttr, ct)),
            AllowsNull: named.NullableAnnotation == NullableAnnotation.Annotated,
            HasProxyCompanion: ReturnTypeClassifier.HasGeneratedProxyCompanion(named, ct));
        return true;
    }

    private static bool TryGetDotBoxDServiceAttribute(
        INamedTypeSymbol named,
        CancellationToken ct,
        out AttributeData serviceAttr)
    {
        foreach (var attr in named.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (ServicesGeneratorTypeNames.IsRpcServiceAttribute(attr.AttributeClass))
            {
                serviceAttr = attr;
                return true;
            }
        }

        serviceAttr = null!;
        return false;
    }

    private static string ReadServiceName(INamedTypeSymbol named, AttributeData serviceAttr, CancellationToken ct)
    {
        var serviceName = named.Name;
        foreach (var arg in serviceAttr.NamedArguments)
        {
            ct.ThrowIfCancellationRequested();

            if (arg.Key == "Name" && arg.Value.Value is string customName)
            {
                serviceName = customName;
            }
        }

        return serviceName;
    }
}
