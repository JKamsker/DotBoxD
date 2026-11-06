using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Infrastructure;

/// <summary>
/// Formats type symbols as fully qualified C# for generated files, replacing the <c>global::</c>
/// qualifier with the extern alias of every type whose assembly is referenced only through extern
/// aliases (for example <c>Lib::Contracts.Payload</c>). The aliases used are collected so the
/// generated file can declare matching <c>extern alias</c> directives.
/// </summary>
internal sealed class ExternAliasTypeFormatter
{
    private const string GlobalAlias = "global";
    private readonly Compilation _compilation;
    private readonly Dictionary<IAssemblySymbol, string?> _aliases =
        new(SymbolEqualityComparer.Default);
    private readonly List<string> _usedAliases = new();

    public ExternAliasTypeFormatter(Compilation compilation)
    {
        _compilation = compilation;
    }

    public IReadOnlyList<string> UsedAliases => _usedAliases;

    public string Format(ITypeSymbol type, SymbolDisplayFormat format, List<string>? usedAliases = null)
    {
        var parts = type.ToDisplayParts(format);
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (IsGlobalQualifier(parts, i) &&
                FirstTypeAfter(parts, i + 2) is { ContainingAssembly: { } assembly } &&
                GetAlias(assembly) is { } alias)
            {
                sb.Append(alias);
                AddDistinct(_usedAliases, alias);
                if (usedAliases is not null)
                {
                    AddDistinct(usedAliases, alias);
                }

                continue;
            }

            sb.Append(parts[i].ToString());
        }

        return sb.ToString();
    }

    private static void AddDistinct(List<string> aliases, string alias)
    {
        if (!aliases.Contains(alias, StringComparer.Ordinal))
        {
            aliases.Add(alias);
        }
    }

    private static bool IsGlobalQualifier(ImmutableArray<SymbolDisplayPart> parts, int index) =>
        parts[index].Kind == SymbolDisplayPartKind.Keyword &&
        parts[index].ToString() == GlobalAlias &&
        index + 1 < parts.Length &&
        parts[index + 1].ToString() == "::";

    private static INamedTypeSymbol? FirstTypeAfter(ImmutableArray<SymbolDisplayPart> parts, int start)
    {
        for (var i = start; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Symbol is INamedTypeSymbol type)
            {
                return type;
            }

            if (part.Kind != SymbolDisplayPartKind.NamespaceName && part.ToString() != ".")
            {
                return null;
            }
        }

        return null;
    }

    private string? GetAlias(IAssemblySymbol assembly)
    {
        if (_aliases.TryGetValue(assembly, out var cached))
        {
            return cached;
        }

        string? alias = null;
        if (_compilation.GetMetadataReference(assembly) is { } reference)
        {
            var aliases = reference.Properties.Aliases;
            if (!aliases.IsDefaultOrEmpty && !aliases.Contains(GlobalAlias, StringComparer.Ordinal))
            {
                alias = aliases[0];
            }
        }

        _aliases[assembly] = alias;
        return alias;
    }
}
