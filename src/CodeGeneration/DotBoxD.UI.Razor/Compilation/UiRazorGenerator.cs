using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotBoxD.UI.Razor;

[Generator(LanguageNames.CSharp)]
public sealed class UiRazorGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Unsupported = new(
        "DBXR001", "Unsupported safe Razor component", "Safe Razor cannot be generated: {0}",
        "DotBoxD.UI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".ui.razor", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, token) => new RazorFile(file.Path, file.GetText(token)?.ToString() ?? ""))
            .Collect().Select(static (files, _) => new EquatableArray<RazorFile>(files));
        var components = context.SyntaxProvider.ForAttributeWithMetadataName(
            "DotBoxD.UI.Authoring.UiRazorComponentAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => Describe(ctx));
        var outputs = components.Combine(files).Select(static (pair, token) => Generate(pair.Left, pair.Right, token))
            .WithTrackingName("UiRazorComponents");
        context.RegisterSourceOutput(outputs, static (output, result) =>
        {
            if (result.Error is { } error)
            {
                var text = SourceText.From(result.FileText);
                var span = new TextSpan(0, 0);
                output.ReportDiagnostic(Diagnostic.Create(Unsupported,
                    Location.Create(result.Path, span, text.Lines.GetLinePositionSpan(span)), error));
            }
            else if (result.Source is { } source)
            { output.AddSource(result.Hint, SourceText.From(source, Encoding.UTF8)); }
        });
    }

    private static Component Describe(GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;
        var attribute = context.Attributes[0];
        var path = attribute.ConstructorArguments[0].Value as string ?? "";
        var render = GetRenderKind(type, context.SemanticModel.Compilation);
        var generationDisabled = attribute.NamedArguments.Any(a => a.Key == "GenerateRender" && a.Value.Value is false);
        var skip = generationDisabled || render == RenderKind.Handwritten;
        var hasRenderProperty = type.GetMembers("Render").OfType<IPropertySymbol>().Any();
        var valid = type.TypeKind == TypeKind.Class && !type.IsFileLocal && !type.IsStatic && !type.IsGenericType && type.ContainingType is null &&
            type.DeclaringSyntaxReferences.All(r => r.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
        return new Component(type.ToDisplayString(), syntax.Identifier.Text,
            type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            string.Join(" ", syntax.Modifiers.Select(m => m.Text)), type.IsRecord ? "record class" : "class",
            path, syntax.SyntaxTree.FilePath, skip, hasRenderProperty, valid,
            render == RenderKind.Incompatible && !generationDisabled
                ? "Render(UiBuilder) must be an instance method returning UiElement, or set GenerateRender = false."
                : null);
    }

    private static RenderKind GetRenderKind(INamedTypeSymbol type, Compilation compilation)
    {
        var componentInterface = compilation.GetTypeByMetadataName("DotBoxD.UI.Authoring.IUiComponent");
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>().Where(m =>
                m.Name == "Render" || IsExplicitComponentRender(m, componentInterface)))
            {
                if (!IsRenderSignature(method) ||
                    !compilation.IsSymbolAccessibleWithin(method, type) && !IsExplicitComponentRender(method, componentInterface))
                { continue; }
                if (!method.IsStatic && method.ReturnType.ToDisplayString() == "DotBoxD.UI.Authoring.UiElement")
                { return RenderKind.Handwritten; }
                if (SymbolEqualityComparer.Default.Equals(current, type))
                { return RenderKind.Incompatible; }
            }
        }
        return RenderKind.None;
    }

    private static bool IsRenderSignature(IMethodSymbol method) => method.Arity == 0 && method.Parameters.Length == 1 &&
        method.Parameters[0].RefKind == RefKind.None && method.Parameters[0].Type.ToDisplayString() == "DotBoxD.UI.Authoring.UiBuilder";

    private static bool IsExplicitComponentRender(IMethodSymbol method, INamedTypeSymbol? componentInterface) =>
        componentInterface is not null && method.ExplicitInterfaceImplementations.Any(i =>
            SymbolEqualityComparer.Default.Equals(i.ContainingType, componentInterface));

    private static Output Generate(Component component, EquatableArray<RazorFile> files, CancellationToken token)
    {
        var hint = component.FullName.Replace("@", "") + ".UiRazor.g.cs";
        if (component.RenderError is { } renderError)
        { return new Output(hint, component.SourcePath, "", null, renderError); }
        if (component.Skip)
        { return new Output(hint, component.SourcePath, "", null, null); }
        if (component.HasRenderProperty)
        {
            return new Output(hint, component.SourcePath, "", null,
                "A component property named Render conflicts with generated Render(UiBuilder). Rename the property or hand-write Render(UiBuilder).");
        }
        if (!component.Valid)
        { return new Output(hint, component.SourcePath, "", null, "Use a top-level, non-generic partial class, or hand-write Render(UiBuilder)."); }
        var matches = files.Where(f => string.Equals(f.Path.Replace('\\', '/'), component.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
            f.Path.Replace('\\', '/').EndsWith("/" + component.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
        { return new Output(hint, component.SourcePath, "", null, "Register exactly one matching .ui.razor file as AdditionalFiles: " + component.Path); }
        var file = matches[0];
        try
        {
            var body = UiRazorDocument.Compile(file.Text, token);
            var ns = component.Namespace.Length == 0 ? "" : "namespace " + component.Namespace + ";\n";
            var source = "// <auto-generated/>\n#nullable enable\n" + ns + component.Modifiers + " " + component.DeclarationKind + " " + component.Name +
                " : global::DotBoxD.UI.Authoring.IUiComponent\n{\npublic global::DotBoxD.UI.Authoring.UiElement Render(global::DotBoxD.UI.Authoring.UiBuilder builder)\n{\n" + body + "\n}\n}\n";
            return new Output(hint, file.Path, file.Text, source, null);
        }
        catch (NotSupportedException error)
        { return new Output(hint, file.Path, file.Text, null, error.Message); }
    }

    private sealed record RazorFile(string Path, string Text);
    private sealed record Component(string FullName, string Name, string Namespace, string Modifiers, string DeclarationKind,
        string Path, string SourcePath, bool Skip, bool HasRenderProperty, bool Valid, string? RenderError);
    private sealed record Output(string Hint, string Path, string FileText, string? Source, string? Error);
    private enum RenderKind { None, Handwritten, Incompatible }
}
