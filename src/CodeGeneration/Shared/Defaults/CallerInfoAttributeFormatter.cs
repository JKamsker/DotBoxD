using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.CodeGeneration.Shared.Defaults;

internal static class CallerInfoAttributeFormatter
{
    public static bool TryAppend(StringBuilder builder, AttributeData attr)
    {
        switch (attr.AttributeClass?.ToDisplayString())
        {
            case "System.Runtime.CompilerServices.CallerMemberNameAttribute":
                builder.Append("[global::System.Runtime.CompilerServices.CallerMemberNameAttribute] ");
                return true;

            case "System.Runtime.CompilerServices.CallerFilePathAttribute":
                builder.Append("[global::System.Runtime.CompilerServices.CallerFilePathAttribute] ");
                return true;

            case "System.Runtime.CompilerServices.CallerLineNumberAttribute":
                builder.Append("[global::System.Runtime.CompilerServices.CallerLineNumberAttribute] ");
                return true;

            case "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute":
                AppendCallerArgumentExpressionAttribute(builder, attr);
                return true;

            default:
                return false;
        }
    }

    private static void AppendCallerArgumentExpressionAttribute(StringBuilder builder, AttributeData attr)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string parameterName)
        {
            return;
        }

        builder
            .Append("[global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute(\"")
            .Append(CSharpLiteralFormatter.EscapeStringLiteral(parameterName))
            .Append("\")] ");
    }
}
