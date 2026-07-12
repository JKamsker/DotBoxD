using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrMarkedCallReader
{
    public static MergeableIrMarkedLoweringCall? TryRead(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        MergeableIrMarkedMethodDetector.ThrowIfUnsupportedExtensionReceiver(method, model.Compilation);
        if (MergeableIrMarkedMethodDetector.IsExtensionReceiver(method) || method.IsStatic)
        {
            return null;
        }

        if (PipelineRoleReader.RoleOf(method, model.Compilation) is not null)
        {
            return null;
        }

        return TryReadIrBodyOf(invocation, model, method) ??
               TryReadLowerToIr(invocation, model, method);
    }

    private static MergeableIrMarkedLoweringCall? TryReadIrBodyOf(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        IMethodSymbol method)
    {
        if (MergeableIrBodyParameterReader.Read(method, model.Compilation) is not { } irBody)
        {
            return null;
        }

        var (inputType, outputType) = IrFuncTypes(irBody, model.Compilation);
        var sourceParameter = SourceParameter(method, irBody);
        ValidateSourceParameterShape(sourceParameter, irBody, inputType, outputType);
        var sourceArgument = RequiredSourceArgument(invocation, method, sourceParameter);
        var irArgument = ArgumentFor(invocation, method, irBody.Parameter);
        if (IsManualIrArgument(irArgument))
        {
            return null;
        }

        ValidateOptionalIrParameter(irBody.Parameter);
        var kind = irBody.HasExplicitKind ? irBody.Kind : InferKind(outputType);
        ValidateDelegateKind(kind, outputType);

        return new MergeableIrMarkedLoweringCall(
            method,
            sourceParameter,
            sourceArgument,
            kind,
            inputType,
            outputType,
            MergeableIrInterceptionKind.IRFuncParameter,
            irBody.Parameter);
    }

    private static (ITypeSymbol Input, ITypeSymbol Output) IrFuncTypes(
        MergeableIrBodyParameter irBody,
        Compilation compilation)
    {
        if (IsIrFunc(irBody.Parameter.Type, compilation, out var inputType, out var outputType))
        {
            return (inputType, outputType);
        }

        throw new NotSupportedException("the [IRBodyOf] parameter must be IRFunc<TInput, TOutput>.");
    }

    private static IParameterSymbol SourceParameter(IMethodSymbol method, MergeableIrBodyParameter irBody)
    {
        var sourceParameter = method.Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, irBody.SourceParameterName, StringComparison.Ordinal));
        return sourceParameter ??
               throw new NotSupportedException(
                   $"the [IRBodyOf] source parameter '{irBody.SourceParameterName}' does not exist.");
    }

    private static void ValidateSourceParameterShape(
        IParameterSymbol sourceParameter,
        MergeableIrBodyParameter irBody,
        ITypeSymbol inputType,
        ITypeSymbol outputType)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceParameter, irBody.Parameter))
        {
            throw new NotSupportedException("the [IRBodyOf] parameter cannot reference itself.");
        }

        if (DelegateTypes(sourceParameter.Type) is not { } delegateTypes)
        {
            throw new NotSupportedException("the [IRBodyOf] source parameter must be Func<TInput, TOutput>.");
        }

        if (!SymbolEqualityComparer.Default.Equals(delegateTypes.Input, inputType) ||
            !SymbolEqualityComparer.Default.Equals(delegateTypes.Output, outputType))
        {
            throw new NotSupportedException(
                "the [IRBodyOf] source delegate type must match the IRFunc<TInput, TOutput> type arguments.");
        }
    }

    private static ArgumentSyntax RequiredSourceArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        IParameterSymbol sourceParameter)
        => ArgumentFor(invocation, method, sourceParameter) ??
           throw new NotSupportedException("the [IRBodyOf] source argument must be supplied at the call site.");

    private static bool IsManualIrArgument(ArgumentSyntax? argument)
        => argument is not null && !IsDefaultIrArgument(argument.Expression);

    private static void ValidateOptionalIrParameter(IParameterSymbol parameter)
    {
        if (!IsOptionalNullDefault(parameter))
        {
            throw new NotSupportedException(
                "the [IRBodyOf] IRFunc parameter must be optional with a null default value.");
        }
    }

    private static MergeableIrMarkedLoweringCall? TryReadLowerToIr(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        IMethodSymbol method)
    {
        if (invocation.ArgumentList.Arguments.Count != 1 ||
            method.Parameters.Length != 1)
        {
            return null;
        }

        var parameter = method.Parameters[0];
        if (MarkedKind(parameter, model.Compilation) is not { } kind)
        {
            return null;
        }

        if (DelegateTypes(parameter.Type, kind) is not { } types)
        {
            throw new NotSupportedException("the marked parameter must be Func<T, bool> or Func<T, TNext>.");
        }

        return new MergeableIrMarkedLoweringCall(
            method,
            parameter,
            invocation.ArgumentList.Arguments[0],
            kind,
            types.Input,
            types.Output,
            MergeableIrInterceptionKind.LoweredPipelineStepOverload,
            null);
    }

    private static MergeableIrLoweredStepKind? MarkedKind(IParameterSymbol parameter, Compilation compilation)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (!MergeableIrAttributeReader.IsDotBoxDAttribute(
                    attribute,
                    compilation,
                    DotBoxDMetadataNames.LowerToIrAttribute) ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not int value)
            {
                continue;
            }

            return MergeableIrStepKindReader.Parse(value);
        }

        return null;
    }

    private static (ITypeSymbol Input, ITypeSymbol Output)? DelegateTypes(
        ITypeSymbol type,
        MergeableIrLoweredStepKind kind)
    {
        if (DelegateTypes(type) is not { } types)
        {
            return null;
        }

        ValidateDelegateKind(kind, types.Output);
        return types;
    }

    private static (ITypeSymbol Input, ITypeSymbol Output)? DelegateTypes(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { Name: "Func", ContainingNamespace: { } ns } func ||
            !string.Equals(ns.ToDisplayString(), "System", StringComparison.Ordinal) ||
            func.TypeArguments.Length != 2)
        {
            return null;
        }

        return (func.TypeArguments[0], func.TypeArguments[1]);
    }

    private static void ValidateDelegateKind(MergeableIrLoweredStepKind kind, ITypeSymbol outputType)
    {
        if (kind == MergeableIrLoweredStepKind.Filter &&
            outputType.SpecialType != SpecialType.System_Boolean)
        {
            throw new NotSupportedException("filter steps must return bool.");
        }
    }

    private static MergeableIrLoweredStepKind InferKind(ITypeSymbol outputType)
        => outputType.SpecialType == SpecialType.System_Boolean
            ? MergeableIrLoweredStepKind.Filter
            : MergeableIrLoweredStepKind.Projection;

    private static bool IsIrFunc(
        ITypeSymbol type,
        Compilation compilation,
        out ITypeSymbol inputType,
        out ITypeSymbol outputType)
    {
        inputType = null!;
        outputType = null!;
        if (type is not INamedTypeSymbol { TypeArguments.Length: 2 } named ||
            compilation.GetTypeByMetadataName(MergeableIrContractNames.IRFunc) is not { } expected ||
            !SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, expected))
        {
            return false;
        }

        inputType = named.TypeArguments[0];
        outputType = named.TypeArguments[1];
        return true;
    }

    private static ArgumentSyntax? ArgumentFor(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        IParameterSymbol parameter)
    {
        var positionalIndex = 0;
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            IParameterSymbol? argumentParameter;
            if (argument.NameColon is { } name)
            {
                argumentParameter = method.Parameters.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name.Name.Identifier.ValueText, StringComparison.Ordinal));
            }
            else
            {
                argumentParameter = positionalIndex < method.Parameters.Length
                    ? method.Parameters[positionalIndex]
                    : null;
                positionalIndex++;
            }

            if (argumentParameter is not null &&
                SymbolEqualityComparer.Default.Equals(argumentParameter, parameter))
            {
                return argument;
            }
        }

        return null;
    }

    private static bool IsDefaultIrArgument(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax or CastExpressionSyntax)
        {
            expression = expression switch
            {
                ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
                CastExpressionSyntax cast => cast.Expression,
                _ => expression
            };
        }

        return expression.IsKind(SyntaxKind.NullLiteralExpression) ||
               expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
               expression is DefaultExpressionSyntax;
    }

    private static bool IsOptionalNullDefault(IParameterSymbol parameter)
        => parameter is { IsOptional: true, HasExplicitDefaultValue: true, ExplicitDefaultValue: null };

}
