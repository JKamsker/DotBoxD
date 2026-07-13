using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private static IReadOnlyList<DotBoxDExpressionModel> LowerHostBindingCallArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        string bindingId,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (TryResolveHandleReceiver(invocation, method, context) is not { } receiver)
        {
            var arguments = LowerHostBindingArguments(
                invocation.ArgumentList.Arguments,
                method.Parameters,
                bindingId,
                lowerExpression);
            return IncludesValueReceiver(method, context.SemanticModel.Compilation)
                ? PrependValueReceiver(invocation, method, bindingId, arguments, lowerExpression)
                : arguments;
        }

        var factoryArguments = LowerHostBindingArguments(
            receiver.Invocation.ArgumentList.Arguments,
            receiver.Method.Parameters,
            bindingId,
            lowerExpression);
        var handleArguments = LowerHostBindingArguments(
            invocation.ArgumentList.Arguments,
            method.Parameters,
            bindingId,
            lowerExpression);
        return factoryArguments.Concat(handleArguments).ToArray();
    }

    private static IReadOnlyList<DotBoxDExpressionModel> PrependValueReceiver(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        string bindingId,
        IReadOnlyList<DotBoxDExpressionModel> arguments,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (method.IsStatic ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            throw new NotSupportedException(
                $"Host binding '{bindingId}' can include only an instance method receiver.");
        }

        var receiver = lowerExpression(memberAccess.Expression);
        return new[] { receiver }.Concat(arguments).ToArray();
    }

    private static bool IncludesValueReceiver(IMethodSymbol method, Compilation compilation)
        => IncludesObjectReceiver(method, compilation) || IncludesExplicitReceiver(method, compilation);

    private static bool IncludesObjectReceiver(IMethodSymbol method, Compilation compilation)
        => IsEligibleHostBindingObjectMethod(method) &&
           !HasHostBindingIgnore(method, compilation) &&
           HostBindingObject(method.ContainingType, compilation) is not null;

    private static bool IncludesExplicitReceiver(IMethodSymbol method, Compilation compilation)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (!IsExplicitHostBinding(attribute, compilation))
            {
                continue;
            }

            return attribute.NamedArguments.Any(argument =>
                string.Equals(argument.Key, "IncludeReceiver", StringComparison.Ordinal) &&
                argument.Value.Value is true);
        }

        return false;
    }

    private static bool IsExplicitHostBinding(AttributeData attribute, Compilation compilation)
        => IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.HostBindingAttribute) &&
           attribute.ConstructorArguments.Length == 3 &&
           attribute.ConstructorArguments[0].Value is string bindingId &&
           !string.IsNullOrWhiteSpace(bindingId);

    private static HostHandleReceiver? TryResolveHandleReceiver(
        InvocationExpressionSyntax invocation,
        IMethodSymbol handleMethod,
        DotBoxDExpressionLoweringContext context)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: InvocationExpressionSyntax receiverInvocation
            } ||
            ResolveHostBindingInvocation(receiverInvocation, context) is not { } receiver ||
            !ReturnsHandleType(receiver.Method.ReturnType, handleMethod.ContainingType))
        {
            return null;
        }

        return new HostHandleReceiver(receiverInvocation, receiver.Method);
    }

    private static bool ReturnsHandleType(ITypeSymbol returnType, INamedTypeSymbol handleType)
    {
        var unwrapped = DotBoxDTypeNameReader.UnwrapTaskLike(returnType);
        return SymbolEqualityComparer.Default.Equals(unwrapped, handleType) ||
               unwrapped is INamedTypeSymbol named &&
               named.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, handleType));
    }

    private readonly record struct HostHandleReceiver(
        InvocationExpressionSyntax Invocation,
        IMethodSymbol Method);
}
