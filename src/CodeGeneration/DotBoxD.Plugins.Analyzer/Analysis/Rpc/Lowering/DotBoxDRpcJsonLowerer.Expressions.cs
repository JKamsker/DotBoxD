using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;
/// <summary>
/// Expression lowering and JSON emission for <see cref="DotBoxDRpcJsonLowerer"/>.
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

        if (TryLowerConstantExpression(expression, out var constant))
        {
            return ApplyNumericConversion(expression, constant);
        }

        var lowered = TryLowerSimpleExpression(expression) ??
                      TryLowerStructuredExpression(expression) ??
                      throw new NotSupportedException($"Server extension expression '{expression}' is not supported.");
        return ApplyNumericConversion(expression, lowered);
    }

    private bool TryLowerConstantExpression(ExpressionSyntax expression, out string lowered)
    {
        lowered = string.Empty;
        if (_model.GetConstantValue(expression, _cancellationToken) is not { HasValue: true } constant)
        {
            return false;
        }

        if (constant.Value is string)
        {
            Allocates = true;
        }

        lowered = LiteralJson(expression, constant.Value);
        return true;
    }

    private string? TryLowerSimpleExpression(ExpressionSyntax expression)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => LowerExpression(parenthesized.Expression),
            AwaitExpressionSyntax awaited => LowerExpression(awaited.Expression),
            IdentifierNameSyntax identifier => LowerIdentifier(identifier),
            PrefixUnaryExpressionSyntax unary => LowerUnary(unary),
            CastExpressionSyntax cast => LowerCast(cast),
            _ => null
        };

    private string? TryLowerStructuredExpression(ExpressionSyntax expression)
        => expression switch
        {
            BinaryExpressionSyntax binary => LowerBinary(binary),
            InvocationExpressionSyntax invocation => LowerInvocation(invocation),
            ObjectCreationExpressionSyntax creation => LowerRecordCreation(creation),
            ImplicitObjectCreationExpressionSyntax creation => LowerRecordCreation(creation),
            ElementAccessExpressionSyntax element => LowerElementAccess(element),
            MemberAccessExpressionSyntax member => LowerMemberAccess(member),
            _ => null
        };

    private string LowerUnary(PrefixUnaryExpressionSyntax unary)
        => unary.Kind() switch
        {
            SyntaxKind.LogicalNotExpression => Obj(("unary", Str("not")), ("operand", LowerExpression(unary.Operand))),
            SyntaxKind.UnaryMinusExpression => Obj(("unary", Str("-")), ("operand", LowerExpression(unary.Operand))),
            SyntaxKind.UnaryPlusExpression => LowerExpression(unary.Operand),
            _ => throw new NotSupportedException($"Server extension unary '{unary.Kind()}' is not supported.")
        };

    private string LowerBinary(BinaryExpressionSyntax binary)
        => LowerBinary(binary, LowerExpression);

    private string LowerBinary(BinaryExpressionSyntax binary, Func<ExpressionSyntax, string> lower)
    {
        if (binary.Kind() == SyntaxKind.AddExpression)
        {
            var leftIsString = IsStringExpression(binary.Left);
            var rightIsString = IsStringExpression(binary.Right);
            if (leftIsString && rightIsString)
            {
                Allocates = true;
                return Call("string.concatBudgeted", null, lower(binary.Left), lower(binary.Right));
            }

            if (leftIsString || rightIsString)
            {
                throw new NotSupportedException(
                    "Server extension operator '+' requires both operands to be strings or matching numeric operands.");
            }
        }

        return BinaryJson(JsonBinaryOperator(binary), lower(binary.Left), lower(binary.Right));
    }

    private string LiteralJson(ExpressionSyntax expression, object? value)
    {
        var converted = _model.GetTypeInfo(expression, _cancellationToken).ConvertedType;
        if (converted is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return EnumLiteralJson(enumType, value);
        }

        if (value is decimal decimalValue)
        {
            return DecimalLiteralJson(decimalValue);
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

        var symbolInfo = _model.GetSymbolInfo(invocation, _cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method &&
            DotBoxDHostBindingExpressionLowerer.HostBinding(method, _model.Compilation) is { } binding)
        {
            AddBindingMetadata(binding);
            var args = LowerArgumentsInParameterOrder(
                invocation.ArgumentList.Arguments,
                method.Parameters,
                $"Host binding '{binding.BindingId}'");
            return Call(binding.BindingId, null, args);
        }
        if (TryLowerServerContextHostBinding(invocation, symbolInfo.Symbol as IMethodSymbol) is { } serverContextCall)
        {
            return serverContextCall;
        }
        if (TryLowerKernelMethodInvocation(invocation) is { } kernelMethod)
        {
            return kernelMethod;
        }

        throw new NotSupportedException($"Server extension call '{invocation}' is not a host binding.");
    }

    private string? TryLowerServerContextHostBinding(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? resolvedMethod)
    {
        if (_serverContextHostBindings.Resolve(invocation, resolvedMethod) is not { } resolved)
        {
            return null;
        }

        AddBindingMetadata(resolved.Binding);
        var args = LowerArgumentsInParameterOrder(
            invocation.ArgumentList.Arguments,
            resolved.Method.Parameters,
            $"Host binding '{resolved.Binding.BindingId}'");
        return Call(resolved.Binding.BindingId, null, args);
    }
    private string LowerMemberAccess(MemberAccessExpressionSyntax member)
    {
        if (TryLowerLiveSettingMemberAccess(member) is { } liveSetting)
        {
            return liveSetting;
        }

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
    internal ITypeSymbol TypeOf(ExpressionSyntax expression)
    {
        if (FallbackLocalType(expression) is { } fallbackLocalType)
        {
            return fallbackLocalType;
        }

        if (_serverContextHostBindings.TryGetContextType(expression) is { } serverContextType)
        {
            return serverContextType;
        }

        var type = _model.GetTypeInfo(expression, _cancellationToken);
        return type.Type
               ?? type.ConvertedType
               ?? throw new NotSupportedException($"Server extension could not resolve the type of '{expression}'.");
    }

    internal bool IsStringExpression(ExpressionSyntax expression)
        => TypeOf(expression).SpecialType == SpecialType.System_String;

    private static bool HasRpcServiceAttribute(ITypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (DotBoxDMetadataNames.IsRpcServiceAttribute(attribute.AttributeClass?.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }

}
