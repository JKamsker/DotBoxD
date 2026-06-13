namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Phase C lowering of an inline hook chain — <c>On&lt;TEvent&gt;().Where?(lambda).InvokeKernel(lambda)</c>
/// — into the same <see cref="PluginKernelModel"/> a kernel class produces, so the existing emitter +
/// verifier path applies unchanged. MVP subset: a single optional <c>Where</c> (no <c>Select</c>),
/// expression-body lambdas, and an <c>InvokeKernel</c> terminal that is a single
/// <c>ctx.Messages.Send(targetId, message)</c>. Any other shape fails safe (returns <c>null</c>, no
/// package), leaving the runtime terminal to throw SGP062 / the analyzer to flag SGP110.
/// </summary>
internal static class HookChainModelFactory
{
    private const string InvokeKernelMethod = "InvokeKernel";
    private const string WhereMethod = "Where";
    private const string OnMethod = "On";
    private const string HookPipelineOriginal = "SafeIR.Plugins.HookPipeline<TEvent>";

    public static PluginKernelModelResult? Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        try
        {
            return TryCreate(invocation, context.SemanticModel, cancellationToken);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static PluginKernelModelResult? TryCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            !string.Equals(terminalAccess.Name.Identifier.ValueText, InvokeKernelMethod, StringComparison.Ordinal))
        {
            return null;
        }

        var receiver = terminalAccess.Expression;
        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol receiverType ||
            !string.Equals(receiverType.OriginalDefinition.ToDisplayString(), HookPipelineOriginal, StringComparison.Ordinal))
        {
            return null;
        }

        if (!TryLambda(invocation, out var terminalLambda) ||
            terminalLambda.ExpressionBody is not InvocationExpressionSyntax sendInvocation)
        {
            return null;
        }

        var (terminalEventParam, terminalContextParam) = LambdaParameters(terminalLambda);
        if (terminalEventParam is null || terminalContextParam is null ||
            !SafeIrHandleModelFactory.IsContextSend(sendInvocation.Expression, terminalContextParam))
        {
            return null;
        }

        if (!TryWalkToSeed(receiver, out var whereLambda, out var seed))
        {
            return null;
        }

        if (model.GetTypeInfo(seed, cancellationToken).Type is not INamedTypeSymbol pipelineType ||
            pipelineType.TypeArguments.Length != 1 ||
            pipelineType.TypeArguments[0] is not INamedTypeSymbol eventType)
        {
            return null;
        }

        var eventProperties = PluginSymbolReader.EventProperties(eventType);
        if (eventProperties.Count == 0)
        {
            return null;
        }

        var (shouldHandle, shouldHandleEventParam, shouldHandleContextParam) =
            LowerShouldHandle(whereLambda, eventProperties, model, cancellationToken);

        var handle = SafeIrHandleModelFactory.CreateFromSend(
            sendInvocation, terminalEventParam, eventProperties, default, model, cancellationToken);

        var chainId = HookChainIdentity.Compute(invocation);
        var kernelName = "HookChain_" + chainId;
        var modelResult = new PluginKernelModel(
            PluginId: "chain-" + chainId,
            Namespace: HookChainIdentity.Namespace(invocation),
            KernelName: kernelName,
            PackageName: kernelName + "PluginPackage",
            EventName: eventType.MetadataName,
            EventParameterName: shouldHandleEventParam,
            ContextParameterName: shouldHandleContextParam,
            HandleEventParameterName: terminalEventParam,
            HandleContextParameterName: terminalContextParam,
            EventProperties: eventProperties,
            LiveSettings: default,
            ShouldHandle: shouldHandle,
            Handle: handle,
            ManifestEffects: SafeIrManifestEffectModel.Create(shouldHandle, handle));

        return new PluginKernelModelResult(modelResult, null);
    }

    private static (SafeIrStatementBodyModel Body, string EventParam, string ContextParam) LowerShouldHandle(
        ParenthesizedLambdaExpressionSyntax? whereLambda,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (whereLambda is null)
        {
            return (
                SafeIrConditionBodyModelFactory.AlwaysTrue(),
                SafeIrGenerationNames.DefaultEventParameterName,
                SafeIrGenerationNames.DefaultContextParameterName);
        }

        if (whereLambda.ExpressionBody is not { } whereBody)
        {
            throw new NotSupportedException("Hook chain Where must be an expression-body lambda.");
        }

        var (eventParam, contextParam) = LambdaParameters(whereLambda);
        if (eventParam is null || contextParam is null)
        {
            throw new NotSupportedException("Hook chain Where lambda must take (element, context).");
        }

        var context = new SafeIrExpressionLoweringContext(eventParam, eventProperties, default, model, cancellationToken);
        return (SafeIrConditionBodyModelFactory.Create(whereBody, context), eventParam, contextParam);
    }

    private static bool TryWalkToSeed(
        ExpressionSyntax receiver,
        out ParenthesizedLambdaExpressionSyntax? whereLambda,
        out InvocationExpressionSyntax seed)
    {
        whereLambda = null;
        seed = null!;
        var current = receiver;
        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax access)
        {
            var name = access.Name.Identifier.ValueText;
            if (string.Equals(name, OnMethod, StringComparison.Ordinal))
            {
                seed = invocation;
                return true;
            }

            if (string.Equals(name, WhereMethod, StringComparison.Ordinal))
            {
                // MVP: a single Where. A second Where (already captured) is unsupported.
                if (whereLambda is not null || !TryLambda(invocation, out var lambda))
                {
                    return false;
                }

                whereLambda = lambda;
                current = access.Expression;
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool TryLambda(InvocationExpressionSyntax invocation, out ParenthesizedLambdaExpressionSyntax lambda)
    {
        lambda = null!;
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1 ||
            arguments[0].Expression is not ParenthesizedLambdaExpressionSyntax parenthesized)
        {
            return false;
        }

        lambda = parenthesized;
        return true;
    }

    private static (string? EventParam, string? ContextParam) LambdaParameters(ParenthesizedLambdaExpressionSyntax lambda)
    {
        var parameters = lambda.ParameterList.Parameters;
        return parameters.Count == 2
            ? (parameters[0].Identifier.ValueText, parameters[1].Identifier.ValueText)
            : (null, null);
    }
}
