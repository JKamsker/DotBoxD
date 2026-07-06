using System.Text;
using System.Threading;
using DotBoxD.CodeGeneration.Shared.Defaults;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
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

            if (CallerInfoAttributeFormatter.TryAppend(attributes, attr))
            {
                continue;
            }

            if (NullableFlowAttributeFormatter.TryAppendInlineAttribute(attributes, attr))
            {
                continue;
            }

            switch (attr.AttributeClass?.ToDisplayString())
            {
                case "System.Runtime.CompilerServices.DateTimeConstantAttribute":
                    if (preserveMetadataDefaultAttributes)
                    {
                        attributes.Append(ParameterDefaultValueEmitter.FormatDateTimeConstantAttribute(parameter));
                    }

                    break;

                case "System.Runtime.CompilerServices.DecimalConstantAttribute":
                    if (preserveMetadataDefaultAttributes)
                    {
                        attributes.Append(ParameterDefaultValueEmitter.FormatDecimalConstantAttribute(parameter));
                    }

                    break;

                case "System.Runtime.InteropServices.DefaultParameterValueAttribute":
                    if (preserveMetadataDefaultAttributes)
                    {
                        attributes.Append(ParameterDefaultValueEmitter.FormatDefaultParameterValueAttribute(parameter));
                    }

                    break;
            }
        }

        return attributes.ToString();
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

}
