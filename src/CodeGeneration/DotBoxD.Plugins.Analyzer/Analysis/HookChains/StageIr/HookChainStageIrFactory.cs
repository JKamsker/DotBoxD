using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainStageIrFactory
{
    private const string CurrentValueName = "$dotboxd.current";

    public static EquatableArray<HookChainStageIrModel> Create(
        InvocationExpressionSyntax terminalInvocation,
        IReadOnlyList<HookChainStage> stages,
        INamedTypeSymbol eventType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (stages.Count == 0)
        {
            return default;
        }

        var results = new List<HookChainStageIrModel>(stages.Count);
        ITypeSymbol currentType = eventType;
        for (var i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            if (TryCreate(terminalInvocation, stage, currentType, model, cancellationToken) is { } lowered)
            {
                results.Add(lowered);
            }

            if (stage.IsSelect)
            {
                if (!TryOutputType(stage, model, cancellationToken, out currentType))
                {
                    break;
                }
            }
        }

        return EquatableArray<HookChainStageIrModel>.FromOwned([.. results]);
    }

    private static HookChainStageIrModel? TryCreate(
        InvocationExpressionSyntax terminalInvocation,
        HookChainStage stage,
        ITypeSymbol inputType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            return Create(terminalInvocation, stage, inputType, model, cancellationToken);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static HookChainStageIrModel Create(
        InvocationExpressionSyntax terminalInvocation,
        HookChainStage stage,
        ITypeSymbol inputType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var (elementParam, contextParam) = LambdaParameters(stage.Lambda);
        if (elementParam is null)
        {
            throw new NotSupportedException();
        }

        var inputTypeSource = SandboxTypeSourceEmitter.TryEmit(inputType)
            ?? throw new NotSupportedException("the stage input type is not wire-eligible.");
        var inputTag = SandboxTypeSourceEmitter.ManifestTag(inputType);
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var current = new DotBoxDExpressionModel(
            $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(CurrentValueName)})",
            inputTag,
            false);
        var context = new DotBoxDExpressionLoweringContext(
            eventParameterName: string.Empty,
            eventProperties: default,
            liveSettings: default,
            model,
            cancellationToken,
            projectedElementName: elementParam,
            projectedElement: current,
            projectedElementType: inputType,
            serverContextParameterName: contextParam,
            serverContextType: contextParam is null ? null : LambdaParameterType(stage.Lambda, contextParam, model, cancellationToken),
            capabilities: capabilities,
            effects: effects);
        var value = DotBoxDExpressionModelFactory.Create(body, context);
        var outputType = stage.IsSelect ? OutputType(stage, model, cancellationToken) : model.Compilation.GetSpecialType(SpecialType.System_Boolean);
        var outputTag = stage.IsSelect ? SandboxTypeSourceEmitter.ManifestTag(outputType) : DotBoxDGenerationNames.ManifestTypes.Bool;
        Validate(stage, value, outputTag);

        var ns = HookChainIdentity.Namespace(terminalInvocation);
        var className = "HookChainStageStep_" + MergeableIrStepIdentity.Compute(stage.Invocation);
        var fullName = string.IsNullOrEmpty(ns)
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix + className
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + ns + "." + className;
        var signature = HookChainStageIrSignatureFactory.Create(stage, model, cancellationToken);

        return new HookChainStageIrModel(
            HintName(ns, className),
            ns,
            className,
            stage.IsSelect ? "Projection" : "Filter",
            inputTag,
            outputTag,
            signature.IRFuncType,
            signature.TypeParameters,
            $"new {DotBoxDGenerationNames.TypeNames.GlobalParameter}({LiteralReader.StringLiteral(CurrentValueName)}, {inputTypeSource})",
            value.Source,
            EquatableArray<string>.FromOwned([.. capabilities]),
            EquatableArray<string>.FromOwned([.. effects]),
            Interception(stage, model, fullName, signature, cancellationToken));
    }

    private static void Validate(HookChainStage stage, DotBoxDExpressionModel value, string outputTag)
    {
        if (!stage.IsSelect && !string.Equals(value.Type, DotBoxDGenerationNames.ManifestTypes.Bool, StringComparison.Ordinal))
        {
            throw new NotSupportedException("filter expressions must lower to bool.");
        }

        if (stage.IsSelect &&
            string.Equals(outputTag, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
        {
            throw new NotSupportedException("the projection output type is not wire-eligible.");
        }
    }

    private static HookChainStageIrInterception Interception(
        HookChainStage stage,
        SemanticModel model,
        string stepFullName,
        HookChainStageIrSignature signature,
        CancellationToken cancellationToken)
    {
        var location = model.GetInterceptableLocation(stage.Invocation, cancellationToken)
            ?? throw new NotSupportedException("the stage call site is not interceptable.");
        var method = model.GetSymbolInfo(stage.Invocation, cancellationToken).Symbol as IMethodSymbol
            ?? throw new NotSupportedException("the stage method could not be resolved.");
        var irParameter = method.Parameters[1];
        return new HookChainStageIrInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            signature.ReceiverType,
            signature.DelegateType,
            signature.ReturnType,
            method.Name,
            signature.MethodTypeArguments,
            stepFullName,
            signature.TypeParameters,
            signature.TypeArguments,
            method.Parameters[0].Name,
            irParameter.Name,
            signature.IRFuncType);
    }

    private static ITypeSymbol OutputType(
        HookChainStage stage,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var typeInfo = model.GetTypeInfo(body, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        return type is { TypeKind: not TypeKind.Error }
            ? type
            : throw new NotSupportedException("the projection output type could not be resolved.");
    }

    private static bool TryOutputType(
        HookChainStage stage,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ITypeSymbol outputType)
    {
        outputType = null!;
        try
        {
            outputType = OutputType(stage, model, cancellationToken);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static (string? ElementParam, string? ContextParam) LambdaParameters(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => (simple.Parameter.Identifier.ValueText, null),
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: var parameters } => parameters.Count switch
            {
                1 => (parameters[0].Identifier.ValueText, null),
                2 => (parameters[0].Identifier.ValueText, parameters[1].Identifier.ValueText),
                _ => (null, null),
            },
            _ => (null, null)
        };

    private static ITypeSymbol? LambdaParameterType(
        LambdaExpressionSyntax lambda,
        string parameterName,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: var parameters })
        {
            foreach (var parameter in parameters)
            {
                if (!string.Equals(parameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
                {
                    continue;
                }

                var type = (model.GetDeclaredSymbol(parameter, cancellationToken) as IParameterSymbol)?.Type;
                return type is { TypeKind: not TypeKind.Error }
                    ? type
                    : GeneratedRemoteHookChainFallback.ServerContextTypeForLambda(lambda, model, cancellationToken);
            }
        }

        return GeneratedRemoteHookChainFallback.ServerContextTypeForLambda(lambda, model, cancellationToken);
    }

    private static string HintName(string ns, string className)
        => string.IsNullOrEmpty(ns)
            ? className + ".g.cs"
            : ns.Replace(DotBoxDGenerationNames.CSharpIdentifiers.EscapePrefix, string.Empty) + "." + className + ".g.cs";

}
