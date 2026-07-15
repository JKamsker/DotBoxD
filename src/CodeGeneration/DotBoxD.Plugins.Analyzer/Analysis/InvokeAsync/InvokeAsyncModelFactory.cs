using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncModelFactory
{
    public static InvokeAsyncResult? Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
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
        catch (NotSupportedException ex)
        {
            return new InvokeAsyncResult(
                null,
                null,
                new PluginKernelDiagnostic(
                    "InvokeAsync call could not be lowered: " + ex.Message,
                    PluginDiagnosticLocation.From(invocation.GetLocation())));
        }
    }

    private static InvokeAsyncResult? TryCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (InvokeAsyncServerSurface.TryCreateLowerToIrMethodDiagnostic(
                model,
                invocation,
                cancellationToken,
                out var diagnostic))
        {
            return new InvokeAsyncResult(null, null, diagnostic);
        }

        if (IsUnqualifiedInvocationExpression(invocation.Expression))
        {
            return TryCreateUnqualified(invocation, model, cancellationToken);
        }

        if (IsConditionalInvocationExpression(invocation.Expression))
        {
            return TryCreateConditional(invocation, model, cancellationToken);
        }

        return TryCreateMemberAccess(invocation, model, cancellationToken);
    }

    private static InvokeAsyncResult? TryCreateUnqualified(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!InvokeAsyncServerSurface.IsLoweringCandidate(model, invocation, cancellationToken))
        {
            return null;
        }

        if (InvokeAsyncServerSurface.BindsToUserInvokeAsync(model, invocation, cancellationToken))
        {
            return null;
        }

        if (InvokeAsyncManualIrArgument.IsExplicit(invocation, model, cancellationToken))
        {
            return null;
        }

        if (InvokeAsyncServerSurface.TryResolveImplicitGeneratedFacade(
                model,
                invocation,
                cancellationToken,
                out var receiverType,
                out var serverAccessType,
                out var worldType))
        {
            return CreateForSurface(
                invocation,
                model,
                cancellationToken,
                receiverType,
                serverAccessType,
                worldType);
        }

        if (InvokeAsyncServerSurface.IsDotBoxDInvokeAsync(model, invocation, cancellationToken))
        {
            throw new NotSupportedException(
                "implicit InvokeAsync calls are not supported; call InvokeAsync on the generated plugin server receiver.");
        }

        return null;
    }

    private static InvokeAsyncResult? TryCreateConditional(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!InvokeAsyncServerSurface.IsLoweringCandidate(model, invocation, cancellationToken))
        {
            return null;
        }

        if (InvokeAsyncServerSurface.BindsToUserInvokeAsync(model, invocation, cancellationToken))
        {
            return null;
        }

        if (InvokeAsyncManualIrArgument.IsExplicit(invocation, model, cancellationToken))
        {
            return null;
        }

        if (IsGeneratedServerConditionalAccess(invocation, model, cancellationToken) ||
            InvokeAsyncServerSurface.IsDotBoxDInvokeAsync(model, invocation, cancellationToken))
        {
            throw new NotSupportedException(
                "conditional access InvokeAsync calls are not supported; check the generated plugin server receiver for null before calling InvokeAsync.");
        }

        return null;
    }

    private static bool IsGeneratedServerConditionalAccess(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
        => invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
           InvokeAsyncServerSurface.TryResolve(
               model,
               conditionalAccess.Expression,
               cancellationToken,
               out _,
               out _,
               out _);

    private static InvokeAsyncResult? TryCreateMemberAccess(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access ||
            !InvokeAsyncServerSurface.IsLoweringCandidate(model, invocation, cancellationToken))
        {
            return null;
        }

        if (InvokeAsyncServerSurface.BindsToUserInvokeAsync(model, invocation, cancellationToken))
        {
            return null;
        }

        if (InvokeAsyncManualIrArgument.IsExplicit(invocation, model, cancellationToken))
        {
            return null;
        }

        if (!InvokeAsyncServerSurface.TryResolve(
                model,
                access.Expression,
                cancellationToken,
                out var receiverType,
                out var serverAccessType,
                out var worldType))
        {
            if (InvokeAsyncServerSurface.IsDotBoxDInvokeAsync(model, invocation, cancellationToken))
            {
                throw new NotSupportedException(
                    "receiver must be a generated plugin server facade or generated server interface, not the erased IPluginServer<TWorld> surface.");
            }

            return null;
        }

        return CreateForSurface(
            invocation,
            model,
            cancellationToken,
            receiverType,
            serverAccessType,
            worldType);
    }

    private static InvokeAsyncResult CreateForSurface(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        string receiverType,
        string? serverAccessType,
        INamedTypeSymbol worldType)
    {
        var shape = InvokeAsyncServerSurface.ResolvedMethod(model, invocation, cancellationToken) is { } method
            ? InvokeAsyncCallShape.Create(invocation, method, model, cancellationToken)
            : null;
        shape ??= InvokeAsyncCallShape.Create(invocation, worldType, model, cancellationToken);
        if (shape is null)
        {
            throw new NotSupportedException(
                "lambda must use a supported block body and capture shape.");
        }

        InvokeAsyncGeneratedTypeValidator.Validate(shape, model.Compilation);

        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var lowerer = new DotBoxDRpcJsonLowerer(
            model,
            capabilities,
            effects,
            cancellationToken,
            serverContextParameterName: shape.WorldParameterName,
            serverContextType: shape.WorldType);
        var bodyJson = shape.LowerBody(lowerer, shape.Block);
        effects.Add(DotBoxDGenerationNames.Effects.Cpu);
        if (lowerer.Allocates)
        {
            effects.Add(DotBoxDGenerationNames.Effects.Alloc);
        }

        var id = HookChainIdentity.Compute(invocation);
        var pluginId = "$anon:" + id;
        var packageName = "InvokeAsync_" + id + DotBoxDGenerationNames.PluginPackageSuffix;
        var ns = HookChainIdentity.Namespace(invocation);
        var interception = Interception(
            invocation,
            model,
            receiverType,
            serverAccessType,
            ns,
            packageName,
            pluginId,
            shape,
            cancellationToken);
        if (interception is null)
        {
            throw new NotSupportedException("call site is not interceptable by the C# compiler.");
        }

        var package = EmitPackage(ns, packageName, pluginId, shape, bodyJson, effects, capabilities);
        return new InvokeAsyncResult(package, interception, null);
    }

}
