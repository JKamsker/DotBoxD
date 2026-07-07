using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.CodeGeneration.Shared.Defaults;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private static readonly Dictionary<string, CallerInfoAttributeAppender> CallerInfoAttributeAppenders = new(StringComparer.Ordinal)
    {
        ["System.Runtime.CompilerServices.DateTimeConstantAttribute"] =
            static (attributes, parameter, _, preserve) => AppendDateTimeConstantAttribute(attributes, parameter, preserve),
        ["System.Runtime.CompilerServices.DecimalConstantAttribute"] =
            static (attributes, parameter, _, preserve) => AppendDecimalConstantAttribute(attributes, parameter, preserve),
        ["System.Runtime.InteropServices.DefaultParameterValueAttribute"] =
            static (attributes, parameter, _, preserve) => AppendDefaultParameterValueAttribute(attributes, parameter, preserve),
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
        if (CallerInfoAttributeFormatter.TryAppend(attributes, attr))
        {
            return;
        }

        if (NullableFlowAttributeFormatter.TryAppendInlineAttribute(attributes, attr))
        {
            return;
        }

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
            NullableFlowAttributeFormatter.TryAppendReturnAttribute(attributes, attr);
        }

        return attributes.ToString();
    }

    private static string BuildMemberAttributePrefix(IMethodSymbol method, CancellationToken ct)
    {
        var attributes = new StringBuilder();
        foreach (var attr in method.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            NullableFlowAttributeFormatter.TryAppendMemberAttribute(attributes, attr);
        }

        return attributes.ToString();
    }

}
