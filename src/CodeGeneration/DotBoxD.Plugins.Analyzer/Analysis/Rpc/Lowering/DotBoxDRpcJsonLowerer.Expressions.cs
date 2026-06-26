using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;
/// <summary>
/// Expression lowering and JSON emission for <see cref="DotBoxDRpcJsonLowerer"/>: constants, identifiers,
/// operators, host-binding calls, <c>list.*</c>/<c>record.*</c> intrinsics, DTO construction, and the
/// small JSON writer the statement half also uses.
/// </summary>
internal sealed partial class DotBoxDRpcJsonLowerer
{
    public string LowerExpression(ExpressionSyntax expression)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_expressionOverride?.Invoke(expression) is { } overridden)
        {
            return ApplyNumericConversion(expression, overridden);
        }

        if (_model.GetConstantValue(expression, _cancellationToken) is { HasValue: true } constant)
        {
            if (constant.Value is string)
            {
                Allocates = true;
            }
            return LiteralJson(expression, constant.Value);
        }
        var lowered = expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => LowerExpression(parenthesized.Expression),
            AwaitExpressionSyntax awaited => LowerExpression(awaited.Expression),
            IdentifierNameSyntax identifier => LowerIdentifier(identifier),
            PrefixUnaryExpressionSyntax unary => LowerUnary(unary),
            BinaryExpressionSyntax binary => BinaryJson(
                JsonBinaryOperator(binary),
                LowerExpression(binary.Left),
                LowerExpression(binary.Right)),
            InvocationExpressionSyntax invocation => LowerInvocation(invocation),
            ObjectCreationExpressionSyntax creation => LowerRecordCreation(creation),
            ElementAccessExpressionSyntax element => LowerElementAccess(element),
            MemberAccessExpressionSyntax member => LowerMemberAccess(member),
            _ => throw new NotSupportedException($"Server extension expression '{expression}' is not supported.")
        };
        return ApplyNumericConversion(expression, lowered);
    }
    private string LowerUnary(PrefixUnaryExpressionSyntax unary)
        => unary.Kind() switch
        {
            SyntaxKind.LogicalNotExpression => Obj(("unary", Str("not")), ("operand", LowerExpression(unary.Operand))),
            SyntaxKind.UnaryMinusExpression => Obj(("unary", Str("-")), ("operand", LowerExpression(unary.Operand))),
            _ => throw new NotSupportedException($"Server extension unary '{unary.Kind()}' is not supported.")
        };
    private string LiteralJson(ExpressionSyntax expression, object? value)
    {
        var converted = _model.GetTypeInfo(expression, _cancellationToken).ConvertedType;
        if (converted?.SpecialType == SpecialType.System_Int64 && value is int i)
        {
            return LiteralJson((long)i);
        }
        if (converted?.SpecialType is SpecialType.System_Double or SpecialType.System_Single &&
            value is IConvertible convertible)
        {
            return LiteralJson(convertible.ToDouble(CultureInfo.InvariantCulture));
        }
        return LiteralJson(value);
    }
    private string LowerInvocation(InvocationExpressionSyntax invocation)
    {
        if (TryLowerServiceHandleInvocation(invocation) is { } serviceHandleCall)
        {
            return serviceHandleCall;
        }
        if (TryLowerMapMethod(invocation) is { } mapCall)
        {
            return mapCall;
        }

        if (_model.GetSymbolInfo(invocation, _cancellationToken).Symbol is IMethodSymbol method &&
            DotBoxDHostBindingExpressionLowerer.HostBinding(method, _model.Compilation) is { } binding)
        {
            AddBindingMetadata(binding);
            var args = LowerArgumentsInParameterOrder(
                invocation.ArgumentList.Arguments,
                method.Parameters,
                $"Host binding '{binding.BindingId}'");
            return Call(binding.BindingId, null, args);
        }
        if (TryLowerKernelMethodInvocation(invocation) is { } kernelMethod)
        {
            return kernelMethod;
        }

        throw new NotSupportedException($"Server extension call '{invocation}' is not a host binding.");
    }
    private string LowerMemberAccess(MemberAccessExpressionSyntax member)
    {
        var receiverType = TypeOf(member.Expression);
        if (string.Equals(member.Name.Identifier.ValueText, "Count", StringComparison.Ordinal) &&
            DotBoxDRpcTypeMapper.ListElementType(receiverType) is not null)
        {
            return Call("list.count", null, LowerExpression(member.Expression));
        }
        if (DotBoxDRpcTypeMapper.ListElementType(receiverType) is not null)
        {
            throw new NotSupportedException($"Server extension list member access '{member}' is not supported.");
        }
        if (receiverType is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            var fields = DotBoxDRpcTypeMapper.RecordFields(named);
            for (var i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i].Name, member.Name.Identifier.ValueText, StringComparison.Ordinal))
                {
                    return Call("record.get", null, LowerExpression(member.Expression), I32(i));
                }
            }
        }
        throw new NotSupportedException($"Server extension member access '{member}' is not supported.");
    }
    private string LowerElementAccess(ElementAccessExpressionSyntax element)
    {
        var receiverType = TypeOf(element.Expression);
        if (element.ArgumentList.Arguments.Count == 1 &&
            DotBoxDRpcTypeMapper.ListElementType(receiverType) is not null)
        {
            return Call("list.get", null, LowerExpression(element.Expression), LowerExpression(element.ArgumentList.Arguments[0].Expression));
        }
        return TryLowerMapElementGet(element, receiverType)
            ?? throw new NotSupportedException($"Server extension indexing '{element}' is not supported.");
    }
    private ITypeSymbol TypeOf(ExpressionSyntax expression)
        => _model.GetTypeInfo(expression, _cancellationToken).Type
           ?? throw new NotSupportedException($"Server extension could not resolve the type of '{expression}'.");

    private static bool HasDotBoxDServiceAttribute(ITypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                "DotBoxD.Services.Attributes.DotBoxDServiceAttribute",
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private string JsonBinaryOperator(BinaryExpressionSyntax binary)
        => binary.Kind() switch
        {
            SyntaxKind.AddExpression => "add",
            SyntaxKind.SubtractExpression => "sub",
            SyntaxKind.MultiplyExpression => "mul",
            SyntaxKind.DivideExpression => "div",
            SyntaxKind.ModuloExpression => "rem",
            SyntaxKind.EqualsExpression => "eq",
            SyntaxKind.NotEqualsExpression => "ne",
            SyntaxKind.LessThanExpression => "lt",
            SyntaxKind.LessThanOrEqualExpression => "lte",
            SyntaxKind.GreaterThanExpression => "gt",
            SyntaxKind.GreaterThanOrEqualExpression => "gte",
            SyntaxKind.LogicalAndExpression => "and",
            SyntaxKind.LogicalOrExpression => "or",
            _ => throw new NotSupportedException($"Server extension operator '{binary.OperatorToken.ValueText}' is not supported.")
        };
}
