using System.Text;
using DotBoxD.CodeGeneration.Shared.Defaults;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcKernelClientParameterSource
{
    public static string ParameterList(IMethodSymbol method)
    {
        var parts = new List<string>();
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var preserveMetadataDefaultAttributes =
                ShouldPreserveMetadataDefaultAttributes(method, i, out var defaultLiteral);
            parts.Add(Declaration(
                method.Parameters[i],
                isLast: i == method.Parameters.Length - 1,
                preserveMetadataDefaultAttributes,
                defaultLiteral));
        }

        return string.Join(", ", parts);
    }

    public static string ArgumentList(IMethodSymbol method)
    {
        var parts = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            parts.Add(Identifier(parameter.Name));
        }

        return string.Join(", ", parts);
    }

    public static string Declaration(IParameterSymbol parameter, bool isLast = false)
    {
        var preserveMetadataDefaultAttributes = ShouldPreserveMetadataDefaultAttributes(
            parameter,
            preserveOptionalAttributeDefault: false,
            out var defaultLiteral);
        return Declaration(parameter, isLast, preserveMetadataDefaultAttributes, defaultLiteral);
    }

    public static string Identifier(string name) => "@" + name;

    private static string ParamsModifier(IParameterSymbol parameter, bool isLast)
        => parameter.IsParams && isLast ? "params " : string.Empty;

    private static string Declaration(
        IParameterSymbol parameter,
        bool isLast,
        bool preserveMetadataDefaultAttributes,
        string? defaultLiteral)
        => AttributePrefix(parameter, preserveMetadataDefaultAttributes) +
           ParamsModifier(parameter, isLast) +
           TypeName(parameter.Type) +
           " " +
           Identifier(parameter.Name) +
           DefaultClause(preserveMetadataDefaultAttributes, defaultLiteral);

    private static bool ShouldPreserveMetadataDefaultAttributes(
        IMethodSymbol method,
        int parameterIndex,
        out string? defaultLiteral)
        => ShouldPreserveMetadataDefaultAttributes(
            method.Parameters[parameterIndex],
            ParameterDefaultValueEmitter.ShouldPreserveOptionalAttributeDefault(method, parameterIndex),
            out defaultLiteral);

    private static bool ShouldPreserveMetadataDefaultAttributes(
        IParameterSymbol parameter,
        bool preserveOptionalAttributeDefault,
        out string? defaultLiteral)
    {
        var hasDefaultValue = ParameterDefaultValueEmitter.HasDefaultValue(parameter);
        defaultLiteral = preserveOptionalAttributeDefault
            ? null
            : ParameterDefaultValueEmitter.FormatSignatureDefaultLiteral(
                parameter,
                hasDefaultValue,
                DefaultLiteralOptions.Analyzer);
        return preserveOptionalAttributeDefault ||
            ParameterDefaultValueEmitter.HasDateTimeConstantAttribute(parameter) ||
            (defaultLiteral is null && HasMetadataDefaultAttribute(parameter));
    }

    private static string DefaultClause(bool preserveMetadataDefaultAttributes, string? defaultLiteral)
        => preserveMetadataDefaultAttributes || defaultLiteral is null ? string.Empty : " = " + defaultLiteral;

    private static string AttributePrefix(IParameterSymbol parameter, bool preserveMetadataDefaultAttributes)
    {
        var attributes = CallerInfoAttributePrefix(parameter);
        if (preserveMetadataDefaultAttributes)
        {
            attributes.Append(
                ParameterDefaultValueEmitter.FormatMetadataDefaultAttributePrefix(
                    parameter,
                    includeOptionalAttribute: true));
        }

        return attributes.ToString();
    }

    private static StringBuilder CallerInfoAttributePrefix(IParameterSymbol parameter)
    {
        var attributes = new StringBuilder();
        foreach (var attribute in parameter.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.Runtime.CompilerServices.CallerMemberNameAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Runtime.CompilerServices.CallerMemberNameAttribute");
                    break;

                case "System.Runtime.CompilerServices.CallerFilePathAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Runtime.CompilerServices.CallerFilePathAttribute");
                    break;

                case "System.Runtime.CompilerServices.CallerLineNumberAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Runtime.CompilerServices.CallerLineNumberAttribute");
                    break;

                case "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute":
                    AppendCallerArgumentExpressionAttribute(attributes, attribute);
                    break;
            }
        }

        return attributes;
    }

    private static void AppendSimpleAttribute(StringBuilder attributes, string attributeType)
    {
        attributes.Append('[')
            .Append(attributeType)
            .Append("] ");
    }

    private static void AppendCallerArgumentExpressionAttribute(StringBuilder attributes, AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string parameterName)
        {
            return;
        }

        attributes.Append("[global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute(")
            .Append(LiteralReader.StringLiteral(parameterName))
            .Append(")] ");
    }

    private static bool HasMetadataDefaultAttribute(IParameterSymbol parameter)
        => ParameterDefaultValueEmitter.HasDateTimeConstantAttribute(parameter) ||
           ParameterDefaultValueEmitter.HasDecimalConstantAttribute(parameter) ||
           ParameterDefaultValueEmitter.HasDefaultParameterValueAttribute(parameter);

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
