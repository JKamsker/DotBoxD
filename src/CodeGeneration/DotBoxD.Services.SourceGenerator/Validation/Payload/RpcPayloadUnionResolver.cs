using System;
using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcPayloadUnionResolver
{
    private const string MessagePackSource = "MessagePack";
    private const string JsonSource = "System.Text.Json";

    public static bool TryRead(
        INamedTypeSymbol baseType,
        string role,
        CancellationToken ct,
        out IReadOnlyList<INamedTypeSymbol> cases,
        out string? reason)
    {
        var builder = new Builder(baseType, role);
        foreach (var attr in baseType.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            var attrName = attr.AttributeClass?.ToDisplayString();
            var attrReason = attrName switch
            {
                ServicesGeneratorTypeNames.MessagePackUnionAttribute => builder.AddMessagePackUnion(attr),
                ServicesGeneratorTypeNames.JsonPolymorphicAttribute => builder.AddJsonPolymorphic(),
                ServicesGeneratorTypeNames.JsonDerivedTypeAttribute => builder.AddJsonDerivedType(attr),
                _ => null,
            };
            if (attrReason is not null)
            {
                cases = Array.Empty<INamedTypeSymbol>();
                reason = attrReason;
                return true;
            }
        }

        return builder.TryBuild(out cases, out reason);
    }

    private sealed class Builder
    {
        private static readonly SymbolDisplayFormat DisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat;

        private readonly INamedTypeSymbol _baseType;
        private readonly string _role;
        private readonly List<INamedTypeSymbol> _cases = new();
        private readonly HashSet<string> _caseKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _discriminatorKeys = new(StringComparer.Ordinal);
        private bool _hasMessagePackUnion;
        private bool _hasJsonPolymorphic;
        private bool _hasJsonDerivedType;

        public Builder(INamedTypeSymbol baseType, string role)
        {
            _baseType = baseType;
            _role = role;
        }

        public string? AddJsonPolymorphic()
        {
            _hasJsonPolymorphic = true;
            return null;
        }

        public string? AddMessagePackUnion(AttributeData attr)
        {
            _hasMessagePackUnion = true;
            var args = attr.ConstructorArguments;
            if (args.Length != 2)
            {
                return InvalidShape(
                    "MessagePack [Union] must declare an int discriminator and derived Type using the Type overload.");
            }

            if (args[0].Value is not int key)
            {
                return InvalidShape("MessagePack [Union] must declare an int stable discriminator.");
            }

            return TryReadNamedType(args[1], out var derivedType)
                ? AddCase(MessagePackSource, "MessagePack [Union]", derivedType, "int:" + key)
                : InvalidShape(
                    "MessagePack [Union] must use the Type-based overload so the derived DTO can be validated at compile time.");
        }

        public string? AddJsonDerivedType(AttributeData attr)
        {
            _hasJsonDerivedType = true;
            var args = attr.ConstructorArguments;
            if (args.Length == 0)
            {
                return InvalidShape("System.Text.Json [JsonDerivedType] must declare a derived Type.");
            }

            if (args.Length < 2 || !TryReadIntOrStringDiscriminator(args[1], out var discriminator))
            {
                return InvalidShape(
                    "System.Text.Json [JsonDerivedType] must declare an int or string stable discriminator.");
            }

            return TryReadNamedType(args[0], out var derivedType)
                ? AddCase(JsonSource, "System.Text.Json [JsonDerivedType]", derivedType, discriminator)
                : InvalidShape("System.Text.Json [JsonDerivedType] must declare a named derived DTO Type.");
        }

        public bool TryBuild(out IReadOnlyList<INamedTypeSymbol> cases, out string? reason)
        {
            var found = _hasMessagePackUnion || _hasJsonPolymorphic || _hasJsonDerivedType;
            if (!found)
            {
                cases = Array.Empty<INamedTypeSymbol>();
                reason = null;
                return false;
            }

            if (_cases.Count == 0)
            {
                cases = Array.Empty<INamedTypeSymbol>();
                reason = InvalidShape("explicit union metadata must declare at least one derived DTO type.");
                return true;
            }

            cases = _cases;
            reason = null;
            return true;
        }

        private string? AddCase(
            string source,
            string sourceLabel,
            INamedTypeSymbol derivedType,
            string discriminator)
        {
            if (!IsClosedConcreteDtoType(derivedType))
            {
                return InvalidShape(
                    sourceLabel + " case '" + derivedType.ToDisplayString() + "' must be a closed concrete DTO type.");
            }

            if (!IsAssignableTo(derivedType, _baseType))
            {
                return InvalidShape(
                    sourceLabel + " case '" + derivedType.ToDisplayString() +
                    "' must derive from or implement union DTO '" + _baseType.ToDisplayString() + "'.");
            }

            if (!_discriminatorKeys.Add(source + ":" + discriminator))
            {
                return InvalidShape(sourceLabel + " declares duplicate discriminator '" + discriminator + "'.");
            }

            var caseKey = source + ":" + derivedType.ToDisplayString(DisplayFormat);
            if (!_caseKeys.Add(caseKey))
            {
                return InvalidShape(sourceLabel + " declares duplicate union case '" + derivedType.ToDisplayString() + "'.");
            }

            _cases.Add(derivedType);
            return null;
        }

        private string InvalidShape(string detail)
            => _role + " uses invalid explicit union DTO '" + _baseType.ToDisplayString() + "': " + detail;

        private static bool TryReadNamedType(TypedConstant constant, out INamedTypeSymbol type)
        {
            if (constant.Kind == TypedConstantKind.Type && constant.Value is INamedTypeSymbol named)
            {
                type = named;
                return true;
            }

            type = null!;
            return false;
        }

        private static bool TryReadIntOrStringDiscriminator(TypedConstant constant, out string discriminator)
        {
            switch (constant.Value)
            {
                case int value:
                    discriminator = "int:" + value;
                    return true;
                case string value:
                    discriminator = "string:" + value;
                    return true;
                default:
                    discriminator = string.Empty;
                    return false;
            }
        }

        private static bool IsClosedConcreteDtoType(INamedTypeSymbol type)
        {
            if (type.IsUnboundGenericType || ContainsTypeParameter(type))
            {
                return false;
            }

            return type.TypeKind switch
            {
                TypeKind.Class => !type.IsAbstract,
                TypeKind.Struct => true,
                _ => false,
            };
        }

        private static bool ContainsTypeParameter(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.TypeParameter)
            {
                return true;
            }

            if (type is IArrayTypeSymbol array)
            {
                return ContainsTypeParameter(array.ElementType);
            }

            if (type is INamedTypeSymbol named)
            {
                foreach (var arg in named.TypeArguments)
                {
                    if (ContainsTypeParameter(arg))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsAssignableTo(INamedTypeSymbol derivedType, INamedTypeSymbol baseType)
        {
            if (SymbolEqualityComparer.Default.Equals(derivedType, baseType))
            {
                return true;
            }

            if (baseType.TypeKind == TypeKind.Interface)
            {
                foreach (var candidate in derivedType.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(candidate, baseType))
                    {
                        return true;
                    }
                }
            }

            for (var current = derivedType.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
