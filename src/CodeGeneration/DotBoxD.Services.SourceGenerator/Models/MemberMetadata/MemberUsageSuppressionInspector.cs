using System;
using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

/// <summary>
/// Collects the diagnostic ids that generated code must suppress because it calls service members
/// marked <c>[Experimental]</c> or <c>[Obsolete]</c> (the dispatcher invokes each method and the
/// registration helpers read each sub-service property), and rejects error-level <c>[Obsolete]</c>
/// members, whose use can never be suppressed.
/// </summary>
internal static class MemberUsageSuppressionInspector
{
    private const string ObsoleteAttributeName = "System.ObsoleteAttribute";
    private const string ExperimentalAttributeName = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";
    private static readonly string[] s_obsoleteWarningIds = { "CS0612", "CS0618" };

    public static UnsupportedMemberDiagnostic? Inspect(
        INamedTypeSymbol interfaceSymbol,
        IEnumerable<IMethodSymbol> methods,
        IEnumerable<IPropertySymbol> properties,
        Compilation compilation,
        CancellationToken ct,
        out MemberUsageSuppressions suppressions)
    {
        var attributeTypes = new AttributeTypes(
            compilation.GetTypeByMetadataName(ObsoleteAttributeName),
            compilation.GetTypeByMetadataName(ExperimentalAttributeName));
        var methodIds = new SortedSet<string>(StringComparer.Ordinal);
        var propertyIds = new SortedSet<string>(StringComparer.Ordinal);
        suppressions = MemberUsageSuppressions.None;

        foreach (var method in methods)
        {
            if (Collect(interfaceSymbol, method, attributeTypes, methodIds, ct) is { } rejected)
            {
                return rejected;
            }
        }

        foreach (var property in properties)
        {
            if (Collect(interfaceSymbol, property, attributeTypes, propertyIds, ct) is { } rejected)
            {
                return rejected;
            }

            if (property.GetMethod is { } getter &&
                Collect(interfaceSymbol, getter, attributeTypes, propertyIds, ct) is { } rejectedGetter)
            {
                return rejectedGetter;
            }
        }

        var allIds = new SortedSet<string>(methodIds, StringComparer.Ordinal);
        allIds.UnionWith(propertyIds);
        suppressions = new MemberUsageSuppressions(allIds.ToEquatableArray(), propertyIds.ToEquatableArray());
        return null;
    }

    private static UnsupportedMemberDiagnostic? Collect(
        INamedTypeSymbol interfaceSymbol,
        ISymbol member,
        AttributeTypes attributeTypes,
        SortedSet<string> ids,
        CancellationToken ct)
    {
        if (CollectFrom(member, attributeTypes, ids, ct))
        {
            return new UnsupportedMemberDiagnostic(
                $"[Obsolete(..., true)] service member '{member.Name}' is not supported because generated dispatcher and registration code must call it",
                DiagnosticLocationFactory.FromSymbol(member));
        }

        // Inherited members are reached through their declaring interface, so its own
        // [Experimental]/[Obsolete] warnings surface in generated code too.
        var declaringType = member.ContainingType;
        if (declaringType is not null &&
            !SymbolEqualityComparer.Default.Equals(declaringType, interfaceSymbol) &&
            CollectFrom(declaringType, attributeTypes, ids, ct))
        {
            return new UnsupportedMemberDiagnostic(
                $"[Obsolete(..., true)] base interface '{declaringType.Name}' is not supported because generated dispatcher and registration code must call its member '{member.Name}'",
                DiagnosticLocationFactory.FromSymbol(member));
        }

        return null;
    }

    // Returns true when the symbol carries an error-level [Obsolete].
    private static bool CollectFrom(
        ISymbol symbol,
        AttributeTypes attributeTypes,
        SortedSet<string> ids,
        CancellationToken ct)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (attribute.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(attributeClass, attributeTypes.Experimental))
            {
                if (attribute.ConstructorArguments.Length > 0 &&
                    attribute.ConstructorArguments[0].Value is string diagnosticId &&
                    !string.IsNullOrWhiteSpace(diagnosticId))
                {
                    ids.Add(diagnosticId);
                }
            }
            else if (SymbolEqualityComparer.Default.Equals(attributeClass, attributeTypes.Obsolete))
            {
                if (IsError(attribute))
                {
                    return true;
                }

                ids.UnionWith(s_obsoleteWarningIds);
                AddCustomObsoleteDiagnosticId(attribute, ids);
            }
        }

        return false;
    }

    private static bool IsError(AttributeData attribute) =>
        attribute.ConstructorArguments.Length > 1 &&
        attribute.ConstructorArguments[1].Value is true;

    private static void AddCustomObsoleteDiagnosticId(AttributeData attribute, SortedSet<string> ids)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "DiagnosticId" &&
                argument.Value.Value is string diagnosticId &&
                !string.IsNullOrWhiteSpace(diagnosticId))
            {
                ids.Add(diagnosticId);
            }
        }
    }

    private sealed record AttributeTypes(INamedTypeSymbol? Obsolete, INamedTypeSymbol? Experimental);
}

/// <summary>
/// Diagnostic ids generated code suppresses for member usage: <see cref="All"/> for code that calls
/// methods and properties (dispatcher), <see cref="Properties"/> for code that reads only sub-service
/// properties (peer registration extensions).
/// </summary>
internal sealed record MemberUsageSuppressions(EquatableArray<string> All, EquatableArray<string> Properties)
{
    public static MemberUsageSuppressions None { get; } =
        new(EquatableArray<string>.Empty, EquatableArray<string>.Empty);
}
