using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.CodeGeneration.Shared.Defaults;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private static readonly Dictionary<string, CallerInfoAttributeAppender> CallerInfoAttributeAppenders = new(StringComparer.Ordinal)
    {
        ["System.Runtime.CompilerServices.CallerMemberNameAttribute"] =
            static (attributes, _, _, _) =>
                attributes.Append("[global::System.Runtime.CompilerServices.CallerMemberNameAttribute] "),
        ["System.Runtime.CompilerServices.CallerFilePathAttribute"] =
            static (attributes, _, _, _) =>
                attributes.Append("[global::System.Runtime.CompilerServices.CallerFilePathAttribute] "),
        ["System.Runtime.CompilerServices.CallerLineNumberAttribute"] =
            static (attributes, _, _, _) =>
                attributes.Append("[global::System.Runtime.CompilerServices.CallerLineNumberAttribute] "),
        ["System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"] =
            static (attributes, _, attr, _) => AppendCallerArgumentExpressionAttribute(attributes, attr),
        ["System.Runtime.CompilerServices.DateTimeConstantAttribute"] =
            static (attributes, parameter, _, preserve) => AppendDateTimeConstantAttribute(attributes, parameter, preserve),
        ["System.Runtime.CompilerServices.DecimalConstantAttribute"] =
            static (attributes, parameter, _, preserve) => AppendDecimalConstantAttribute(attributes, parameter, preserve),
        ["System.Runtime.InteropServices.DefaultParameterValueAttribute"] =
            static (attributes, parameter, _, preserve) => AppendDefaultParameterValueAttribute(attributes, parameter, preserve),
        ["System.Diagnostics.CodeAnalysis.AllowNullAttribute"] =
            static (attributes, _, _, _) =>
                AppendSimpleAttribute(attributes, "global::System.Diagnostics.CodeAnalysis.AllowNullAttribute"),
        ["System.Diagnostics.CodeAnalysis.DisallowNullAttribute"] =
            static (attributes, _, _, _) =>
                AppendSimpleAttribute(attributes, "global::System.Diagnostics.CodeAnalysis.DisallowNullAttribute"),
        ["System.Diagnostics.CodeAnalysis.MaybeNullAttribute"] =
            static (attributes, _, _, _) =>
                AppendSimpleAttribute(attributes, "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute"),
        ["System.Diagnostics.CodeAnalysis.NotNullAttribute"] =
            static (attributes, _, _, _) =>
                AppendSimpleAttribute(attributes, "global::System.Diagnostics.CodeAnalysis.NotNullAttribute"),
        ["System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute"] =
            static (attributes, _, attr, _) =>
                AppendBooleanArgumentAttribute(attributes, attr, "global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute"),
        ["System.Diagnostics.CodeAnalysis.NotNullWhenAttribute"] =
            static (attributes, _, attr, _) =>
                AppendBooleanArgumentAttribute(attributes, attr, "global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute"),
        ["System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute"] =
            static (attributes, _, attr, _) =>
                AppendStringArgumentAttribute(attributes, attr, "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute"),
    };

    private delegate void CallerInfoAttributeAppender(
        StringBuilder attributes,
        IParameterSymbol parameter,
        AttributeData attr,
        bool preserveMetadataDefaultAttributes);

    private static string BuildCallerInfoAttributePrefix(
        IParameterSymbol parameter,
        CancellationToken ct,
        bool preserveOptionalAttributeDefault,
        bool preserveMetadataDefaultAttributes)
    {
        var attributes = new StringBuilder();
        var hasDateTimeConstant = preserveMetadataDefaultAttributes &&
            ParameterDefaultValueEmitter.HasDateTimeConstantAttribute(parameter);
        var hasDecimalConstant = preserveMetadataDefaultAttributes &&
            ParameterDefaultValueEmitter.HasDecimalConstantAttribute(parameter);
        var hasDefaultParameterValue = preserveMetadataDefaultAttributes &&
            ParameterDefaultValueEmitter.HasDefaultParameterValueAttribute(parameter);
        var preserveOptionalAttribute =
            preserveOptionalAttributeDefault ||
            hasDateTimeConstant ||
            hasDecimalConstant ||
            hasDefaultParameterValue;
        if (preserveOptionalAttribute)
        {
            attributes.Append("[global::System.Runtime.InteropServices.OptionalAttribute] ");
        }

        foreach (var attr in parameter.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            AppendCallerInfoAttribute(attributes, parameter, attr, preserveMetadataDefaultAttributes);
        }

        return attributes.ToString();
    }

    private static void AppendCallerInfoAttribute(
        StringBuilder attributes,
        IParameterSymbol parameter,
        AttributeData attr,
        bool preserveMetadataDefaultAttributes)
    {
        var name = attr.AttributeClass?.ToDisplayString();
        if (name is not null && CallerInfoAttributeAppenders.TryGetValue(name, out var append))
        {
            append(attributes, parameter, attr, preserveMetadataDefaultAttributes);
        }
    }

    private static void AppendDateTimeConstantAttribute(StringBuilder attributes, IParameterSymbol parameter, bool preserve)
    {
        if (preserve)
        {
            attributes.Append(ParameterDefaultValueEmitter.FormatDateTimeConstantAttribute(parameter));
        }
    }

    private static void AppendDecimalConstantAttribute(StringBuilder attributes, IParameterSymbol parameter, bool preserve)
    {
        if (preserve)
        {
            attributes.Append(ParameterDefaultValueEmitter.FormatDecimalConstantAttribute(parameter));
        }
    }

    private static void AppendDefaultParameterValueAttribute(StringBuilder attributes, IParameterSymbol parameter, bool preserve)
    {
        if (preserve)
        {
            attributes.Append(ParameterDefaultValueEmitter.FormatDefaultParameterValueAttribute(parameter));
        }
    }

    private static string BuildReturnFlowAttributePrefix(IMethodSymbol method, CancellationToken ct)
    {
        var attributes = new StringBuilder();
        foreach (var attr in method.GetReturnTypeAttributes())
        {
            ct.ThrowIfCancellationRequested();

            switch (attr.AttributeClass?.ToDisplayString())
            {
                case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                    AppendReturnSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                    AppendReturnSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.NotNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                    AppendReturnStringArgumentAttribute(
                        attributes,
                        attr,
                        "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute");
                    break;
            }
        }

        return attributes.ToString();
    }

    private static void AppendSimpleAttribute(StringBuilder sb, string attributeType)
    {
        sb.Append("[")
            .Append(attributeType)
            .Append("] ");
    }

    private static void AppendBooleanArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not bool value)
        {
            return;
        }

        sb.Append("[")
            .Append(attributeType)
            .Append("(")
            .Append(value ? "true" : "false")
            .Append(")] ");
    }

    private static void AppendStringArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string value)
        {
            return;
        }

        sb.Append("[")
            .Append(attributeType)
            .Append("(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(value))
            .Append("\")] ");
    }

    private static void AppendReturnSimpleAttribute(StringBuilder sb, string attributeType)
    {
        sb.Append("[return: ")
            .Append(attributeType)
            .AppendLine("]");
    }

    private static void AppendReturnStringArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string value)
        {
            return;
        }

        sb.Append("[return: ")
            .Append(attributeType)
            .Append("(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(value))
            .AppendLine("\")]");
    }

    private static void AppendCallerArgumentExpressionAttribute(StringBuilder sb, AttributeData attr)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string parameterName)
        {
            return;
        }

        sb.Append("[global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(parameterName))
            .Append("\")] ");
    }

}
