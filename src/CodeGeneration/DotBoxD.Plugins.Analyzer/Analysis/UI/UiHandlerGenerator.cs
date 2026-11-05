using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.UI;

[Generator(LanguageNames.CSharp)]
public sealed class UiHandlerGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor Unsupported = new(
        "DBXU001", "Unsupported sandbox UI handler", "UI handler '{0}' cannot be lowered: {1}. Use [UiRemoteHandler] with typed RPC, or an explicit host binding.",
        "DotBoxD.UI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        Register(context, "DotBoxD.UI.Authoring.UiLocalHandlerAttribute", remote: false);
        Register(context, "DotBoxD.UI.Authoring.UiRemoteHandlerAttribute", remote: true);
        RegisterUnsupportedTargets(context, "DotBoxD.UI.Authoring.UiLocalHandlerAttribute");
        RegisterUnsupportedTargets(context, "DotBoxD.UI.Authoring.UiRemoteHandlerAttribute");
    }

    private static void Register(IncrementalGeneratorInitializationContext context, string attribute, bool remote)
    {
        var handlers = GeneratorGuard.AttributeValues(context, attribute,
            static (node, _) => node is MethodDeclarationSyntax,
            "sandbox UI handler", (ctx, token) => UiHandlerModelFactory.Create(ctx, remote, token))
            .WithTrackingName(remote ? "UiRemoteHandlers" : "UiLocalHandlers");
        context.RegisterSourceOutput(handlers, static (output, model) =>
        {
            if (model.Error is { } error)
            {
                var location = Location.Create(model.Path, model.Span, model.LineSpan);
                output.ReportDiagnostic(Diagnostic.Create(Unsupported, location, model.MethodName, error));
            }
            else if (model.Source is { } source)
            { output.AddSource(model.HintName, SourceText.From(source, Encoding.UTF8)); }
        });
    }

    private static void RegisterUnsupportedTargets(IncrementalGeneratorInitializationContext context, string attribute)
    {
        var diagnostics = GeneratorGuard.SyntaxValues(
            context,
            static (node, _) => node is AttributeSyntax,
            "sandbox UI handler non-ordinary target",
            (syntaxContext, token) => UnsupportedTargetDiagnostic(syntaxContext, attribute, token));
        GeneratorGuard.RegisterOutput(
            context,
            diagnostics,
            "sandbox UI handler non-ordinary target diagnostic output",
            static (output, diagnostic) => output.ReportDiagnostic(diagnostic));
    }

    private static Diagnostic? UnsupportedTargetDiagnostic(GeneratorSyntaxContext context, string attribute, CancellationToken token)
    {
        if (context.Node is not AttributeSyntax { Parent.Parent: SyntaxNode target } syntax ||
            target is MethodDeclarationSyntax ||
            context.SemanticModel.GetSymbolInfo(syntax, token).Symbol is not IMethodSymbol constructor ||
            constructor.ContainingType.ToDisplayString() != attribute)
        { return null; }
        var name = (context.SemanticModel.GetDeclaredSymbol(target, token) as IMethodSymbol)?.Name ?? target.GetType().Name;
        return Diagnostic.Create(Unsupported, target.GetLocation(), name,
            "UI handlers must be ordinary methods declared directly on a partial class or struct");
    }
}
