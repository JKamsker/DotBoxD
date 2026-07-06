using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// Phase C lowering of an inline hook chain —
/// <c>On&lt;TEvent&gt;().Where*(lambda).Select*(lambda).Run(lambda)</c> — into the same
/// <see cref="PluginKernelModel"/> a kernel class produces, so the existing emitter + verifier path
/// applies unchanged. The <c>Where</c>s AND-compose into <c>ShouldHandle</c>; a <c>Select</c> projects
/// the flowing element and downstream lambdas substitute that projection at compile time (via the
/// lowering context's projected-element binding); the <c>Run</c> terminal's single
/// <c>ctx.Messages.Send(targetId, message)</c> becomes <c>Handle</c>. Supported subset: expression-body
/// lambdas and a direct Send terminal or static <c>[KernelMethod]</c> Send helper. Any other shape fails safe
/// (returns <c>null</c>, no package), leaving the runtime terminal to throw DBXK062 / the generator to report DBXK114.
/// </summary>
internal static partial class HookChainModelFactory
{
    public static HookChainCreateResult? Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        HookChainResult? chain;
        string? notLoweredDetail = null;
        try
        {
            chain = TryCreate(invocation, context.SemanticModel, cancellationToken);
        }
        catch (KernelMethodArgumentReuseException ex)
        {
            return new HookChainCreateResult(
                null,
                null,
                new PluginKernelDiagnostic(
                    ex.Message,
                    ex.Location ?? PluginDiagnosticLocation.From(invocation.GetLocation())));
        }
        catch (UnsupportedHookChainEventTypeException ex)
        {
            return new HookChainCreateResult(null, null, new PluginKernelDiagnostic(ex.Message, ex.Location));
        }
        catch (HookChainUnsupportedDiagnosticException ex)
        {
            return new HookChainCreateResult(null, null, ex.Diagnostic);
        }
        catch (NotSupportedException ex)
        {
            chain = null;
            notLoweredDetail = ex.Message;
        }
        if (chain is not null)
        {
            return new HookChainCreateResult(chain, null);
        }
        return NotLoweredDiagnostic(invocation, context.SemanticModel, cancellationToken, notLoweredDetail);
    }

    private static HookChainResult? TryCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!TryPrepareChain(invocation, model, cancellationToken, out var prepared))
        {
            return null;
        }

        prepared.Stages.Reverse(); // seed-to-terminal order

        if (!TryEventShape(prepared.Seed, model, cancellationToken, out var eventShape))
        {
            return null;
        }

        // Result-returning hooks (Register/RegisterLocal) lower the filter the same way, but the Handle returns
        // the result record (Register) or Unit with an in-process delegate (RegisterLocal); they install via the
        // result-chain entrypoints. Delegated to keep the Send-terminal path below focused.
        return IsResultTerminal(prepared.InstallKind)
            ? BuildResultHookChain(invocation, model, cancellationToken, prepared, eventShape)
            : BuildSendHookChain(invocation, model, cancellationToken, prepared, eventShape);
    }
}
