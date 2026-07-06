using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static readonly HashSet<string> PrimitiveLiteralHelpers = new(StringComparer.Ordinal)
    {
        "Str",
        "I32",
        "I64",
        "F64",
        "Bool",
    };

    private static readonly Dictionary<string, InvocationShapeResolver> InvocationShapeResolvers = new(StringComparer.Ordinal)
    {
        ["Not"] = static (args, _) => Unary(args, DotBoxDGenerationNames.ManifestTypes.Bool, DotBoxDGenerationNames.ManifestTypes.Bool),
        ["Int32ToStr"] = static (args, _) =>
            Unary(args, DotBoxDGenerationNames.ManifestTypes.Int, DotBoxDGenerationNames.ManifestTypes.String, allocates: true),
        ["StringLength"] = static (args, _) =>
            Unary(args, DotBoxDGenerationNames.ManifestTypes.String, DotBoxDGenerationNames.ManifestTypes.Int),
        ["StringSubstring"] = static (args, _) => StringSubstring(args),
        ["ConcatString"] = static (args, _) =>
            Binary(args, DotBoxDGenerationNames.ManifestTypes.String, DotBoxDGenerationNames.ManifestTypes.String, allocates: true),
        ["StringEquals"] = static (args, _) =>
            Binary(args, DotBoxDGenerationNames.ManifestTypes.String, DotBoxDGenerationNames.ManifestTypes.Bool),
        ["Neg"] = static (args, _) => NumericUnary(args),
        ["Eq"] = static (args, _) => EqualityBinary(args),
        ["Ne"] = static (args, _) => EqualityBinary(args),
        ["Ge"] = static (args, _) => NumericBinary(args, comparison: true),
        ["Gt"] = static (args, _) => NumericBinary(args, comparison: true),
        ["Le"] = static (args, _) => NumericBinary(args, comparison: true),
        ["Lt"] = static (args, _) => NumericBinary(args, comparison: true),
        ["And"] = static (args, _) =>
            Binary(args, DotBoxDGenerationNames.ManifestTypes.Bool, DotBoxDGenerationNames.ManifestTypes.Bool),
        ["Or"] = static (args, _) =>
            Binary(args, DotBoxDGenerationNames.ManifestTypes.Bool, DotBoxDGenerationNames.ManifestTypes.Bool),
        ["Add"] = static (args, _) => NumericBinary(args, comparison: false),
        ["Sub"] = static (args, _) => NumericBinary(args, comparison: false),
        ["Mul"] = static (args, _) => NumericBinary(args, comparison: false),
        ["Div"] = static (args, _) => NumericBinary(args, comparison: false),
        ["Mod"] = static (args, _) => NumericBinary(args, comparison: false),
    };

    private delegate DescriptorShape InvocationShapeResolver(DescriptorShape[] args, bool allocates);

    private static DescriptorShape RevalidateDescriptorShape(
        IMethodSymbol method,
        DotBoxDExpressionLoweringContext context,
        KernelMethodDescriptorPayload descriptor)
    {
        var expression = SyntaxFactory.ParseExpression(descriptor.Source);
        if (expression.ContainsDiagnostics)
        {
            throw new NotSupportedException("Generated descriptor contains invalid expression source.");
        }

        ValidateDescriptorSourceShape(expression);
        var parameters = descriptor.Parameters.ToDictionary(
            static parameter => parameter.Placeholder,
            static parameter => DescriptorShape.Simple(parameter.Type),
            StringComparer.Ordinal);
        var shape = InferDescriptorShape(
            expression,
            method.ContainingType,
            context.SemanticModel.Compilation,
            parameters);
        if (!string.Equals(shape.Type, descriptor.ReturnType, StringComparison.Ordinal) ||
            shape.Allocates != descriptor.Allocates)
        {
            throw new NotSupportedException(
                $"Generated descriptor for context [KernelMethod] '{method.Name}' has stale return metadata.");
        }

        return shape;
    }

    private static DescriptorShape InferDescriptorShape(
        ExpressionSyntax expression,
        INamedTypeSymbol contextType,
        Compilation compilation,
        IReadOnlyDictionary<string, DescriptorShape> parameters)
        => expression switch
        {
            IdentifierNameSyntax identifier when parameters.TryGetValue(identifier.Identifier.ValueText, out var shape) =>
                shape,
            InvocationExpressionSyntax invocation =>
                InferInvocationShape(invocation, contextType, compilation, parameters),
            ObjectCreationExpressionSyntax creation when IsCallExpression(creation.Type) =>
                InferCallExpressionShape(creation, contextType, compilation, parameters),
            _ => throw new NotSupportedException("Generated descriptor contains unsupported expression source.")
        };

    private static DescriptorShape InferInvocationShape(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol contextType,
        Compilation compilation,
        IReadOnlyDictionary<string, DescriptorShape> parameters)
    {
        var name = InvocationName(invocation.Expression);
        if (PrimitiveLiteralHelpers.Contains(name))
        {
            return PrimitiveLiteralShape(name, invocation.ArgumentList.Arguments);
        }

        var args = DescriptorHelperArguments(invocation)
            .Select(argument => InferDescriptorShape(argument.Expression, contextType, compilation, parameters))
            .ToArray();
        var allocates = args.Any(static arg => arg.Allocates);
        if (InvocationShapeResolvers.TryGetValue(name, out var resolver))
        {
            return resolver(args, allocates);
        }

        throw new NotSupportedException("Generated descriptor contains unsupported expression source.");
    }

    private static DescriptorShape InferCallExpressionShape(
        ObjectCreationExpressionSyntax creation,
        INamedTypeSymbol contextType,
        Compilation compilation,
        IReadOnlyDictionary<string, DescriptorShape> parameters)
    {
        var bindingId = CallBindingId(creation);
        var argumentExpressions = CallArguments(creation).ToArray();
        var args = argumentExpressions
            .Select(argument => InferDescriptorShape(argument, contextType, compilation, parameters))
            .ToArray();
        var allocates = args.Any(static arg => arg.Allocates);
        if (HostBindingRequirements(contextType, compilation, bindingId) is { } requirements)
        {
            RequireCallGenericNull(creation, bindingId);
            if (args.Length != requirements.ParameterShapes.Count ||
                args.Where((arg, index) => !SameDescriptorType(arg, requirements.ParameterShapes[index])).Any())
            {
                throw new NotSupportedException("Generated descriptor contains a host call with stale argument metadata.");
            }

            return requirements.ReturnShape.WithAllocates(allocates || HostReturnAllocates(requirements.ReturnShape));
        }

        return bindingId switch
        {
            "list.count" => ListCountShape(creation, args, allocates),
            "record.new" => RecordNewShape(creation, args),
            "record.get" => RecordGetShape(creation, args, argumentExpressions, allocates),
            _ => throw new NotSupportedException(
                $"Generated descriptor contains an untrusted host call '{bindingId}'.")
        };
    }

    private static string InvocationName(ExpressionSyntax expression)
        => expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => throw new NotSupportedException("Generated descriptor contains unsupported expression source.")
        };

    private static string CallBindingId(ObjectCreationExpressionSyntax creation)
    {
        var arguments = CallConstructorArguments(creation);
        if (arguments.Length >= 1 &&
            arguments[0].Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression) &&
            literal.Token.ValueText is { Length: > 0 } bindingId)
        {
            return bindingId;
        }

        throw new NotSupportedException("Generated descriptor contains a non-literal host call binding id.");
    }

    private static IEnumerable<ExpressionSyntax> CallArguments(ObjectCreationExpressionSyntax creation)
    {
        var arguments = CallConstructorArguments(creation);
        if (arguments.Length < 2)
        {
            throw new NotSupportedException("Generated descriptor contains a malformed host call.");
        }

        return arguments[1].Expression switch
        {
            CollectionExpressionSyntax collection => CollectionArguments(collection),
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
            _ => throw new NotSupportedException("Generated descriptor contains unsupported host call arguments.")
        };
    }

    private static IEnumerable<ExpressionSyntax> CollectionArguments(CollectionExpressionSyntax collection)
    {
        foreach (var element in collection.Elements)
        {
            if (element is not ExpressionElementSyntax expressionElement)
            {
                throw new NotSupportedException("Generated descriptor contains unsupported host call arguments.");
            }

            yield return expressionElement.Expression;
        }
    }

    private static ArgumentSyntax[] CallConstructorArguments(ObjectCreationExpressionSyntax creation)
    {
        var arguments = creation.ArgumentList?.Arguments;
        if (arguments is not { Count: > 0 })
        {
            throw new NotSupportedException("Generated descriptor contains a malformed host call.");
        }

        var result = new ArgumentSyntax[arguments.Value.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var argument = arguments.Value[i];
            if (argument.NameColon is not null ||
                !argument.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                throw new NotSupportedException("Generated descriptor contains a malformed host call.");
            }

            result[i] = argument;
        }

        return result;
    }

    private static IEnumerable<ArgumentSyntax> DescriptorHelperArguments(InvocationExpressionSyntax invocation)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon is not null ||
                !argument.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                throw new NotSupportedException("Generated descriptor contains stale helper argument metadata.");
            }

            yield return argument;
        }
    }
}
