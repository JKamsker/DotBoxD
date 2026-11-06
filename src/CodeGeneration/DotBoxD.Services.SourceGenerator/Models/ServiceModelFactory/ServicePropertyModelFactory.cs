using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Generation;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class ServicePropertyModelFactory
{
    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static bool TryBuild(
        IPropertySymbol propertySymbol,
        Compilation compilation,
        CancellationToken ct,
        out ServicePropertyModel property,
        out DiagnosticLocation location)
    {
        ct.ThrowIfCancellationRequested();

        location = DiagnosticLocationFactory.FromSymbol(propertySymbol);
        var typeFormatter = new ExternAliasTypeFormatter(compilation);
        string FormatType(ITypeSymbol type) => typeFormatter.Format(type, s_qualifiedFormat);
        if (ServiceShapeValidator.IsInstanceIdProperty(propertySymbol))
        {
            property = new ServicePropertyModel(
                IdentifierHelpers.EscapeIdentifier(propertySymbol.Name),
                FormatType(propertySymbol.ContainingType),
                FormatType(propertySymbol.Type),
                ProxyType: null,
                BuildPropertyAttributePrefix(propertySymbol, ct),
                IsInstanceId: true,
                SubService: null,
                MemberAttributePrefix: MemberAttributeFormatter.BuildPrefix(propertySymbol, compilation, ct),
                ExternAliases: typeFormatter.UsedAliases.ToEquatableArray());
            return true;
        }

        if (propertySymbol.Type is not INamedTypeSymbol propertyType)
        {
            property = null!;
            location = default;
            return false;
        }

        SubServiceReturnTypeReader.TryGetSubServiceInfo(propertySymbol.Type, ct, out var subService, FormatType);
        var proxyType = ProxyGenerationHelpers.BuildSubProxyTypeName(
            FormatType(propertyType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)));
        property = new ServicePropertyModel(
            IdentifierHelpers.EscapeIdentifier(propertySymbol.Name),
            FormatType(propertySymbol.ContainingType),
            FormatType(propertyType),
            proxyType,
            BuildPropertyAttributePrefix(propertySymbol, ct),
            IsInstanceId: false,
            subService,
            MemberAttributeFormatter.BuildPrefix(propertySymbol, compilation, ct),
            typeFormatter.UsedAliases.ToEquatableArray());
        return true;
    }

    private static string BuildPropertyAttributePrefix(IPropertySymbol property, CancellationToken ct)
    {
        var attributes = new StringBuilder();
        foreach (var attr in property.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            NullableFlowAttributeFormatter.TryAppendMemberAttribute(attributes, attr);
        }

        return attributes.ToString();
    }
}
