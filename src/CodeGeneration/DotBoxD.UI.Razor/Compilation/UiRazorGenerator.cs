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
            if (result.Source is { } source)
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
        var hasRenderProperty = HasRenderNameCollision(type, context.SemanticModel.Compilation);
        var valid = IsSupportedContainer(type);
        return new Component(type.ToDisplayString(), syntax.Identifier.Text,
            type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(), type,
            context.SemanticModel.Compilation,
            string.Join(" ", syntax.Modifiers.Select(m => m.Text)), type.IsRecord ? "record class" : "class",
            path, syntax.SyntaxTree.FilePath, generationDisabled, render == RenderKind.Handwritten, hasRenderProperty, valid,
            EndpointConstants(type, context.SemanticModel.Compilation),
            render == RenderKind.Incompatible && !generationDisabled
                ? "Render(UiBuilder) must be an instance method returning UiElement, or set GenerateRender = false."
                : null);
    }

    private static EquatableArray<string> EndpointConstants(INamedTypeSymbol type, Compilation compilation)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var field in current.GetMembers().OfType<IFieldSymbol>().Where(field => IsEndpointConstant(field, type, compilation)))
            { AddEndpointName(names, type, field.Name); }
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>().Where(method => GeneratesRemoteEndpoint(method, type, compilation)))
            { AddEndpointName(names, type, method.Name + "UiEndpoint"); }
        }
        return new EquatableArray<string>(names);
    }

    private static bool IsEndpointConstant(IFieldSymbol field, INamedTypeSymbol type, Compilation compilation)
        => field.IsConst && field.Type.SpecialType == SpecialType.System_Int32 &&
            field.ConstantValue is int value && value > 0 && compilation.IsSymbolAccessibleWithin(field, type);

    private static bool GeneratesRemoteEndpoint(IMethodSymbol method, INamedTypeSymbol type, Compilation compilation)
    {
        var remote = method.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "DotBoxD.UI.Authoring.UiRemoteHandlerAttribute");
        return remote is not null && compilation.IsSymbolAccessibleWithin(method, type) && !remote.NamedArguments.Any(argument =>
            argument.Key == "GenerateEndpoint" && argument.Value.Value is false);
    }

    private static void AddEndpointName(ISet<string> names, INamedTypeSymbol type, string name)
    {
        names.Add(name);
        names.Add(type.Name + "." + name);
    }

    private static bool HasRenderNameCollision(INamedTypeSymbol type, Compilation compilation)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers("Render").Any(member => member is not IMethodSymbol &&
                compilation.IsSymbolAccessibleWithin(member, type)))
            { return true; }
        }
        return false;
    }

    internal static bool GeneratesComponentImplementation(INamedTypeSymbol type, Compilation compilation)
        => IsSupportedContainer(type) && !HasRenderNameCollision(type, compilation) && GetRenderKind(type, compilation) == RenderKind.None &&
            type.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "DotBoxD.UI.Authoring.UiRazorComponentAttribute" &&
                !attribute.NamedArguments.Any(argument => argument.Key == "GenerateRender" && argument.Value.Value is false));

    private static bool IsSupportedContainer(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Class && !type.IsFileLocal && !type.IsStatic && !type.IsGenericType && type.ContainingType is null &&
            type.DeclaringSyntaxReferences.All(r => r.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static RenderKind GetRenderKind(INamedTypeSymbol type, Compilation compilation)
    {
        var componentInterface = compilation.GetTypeByMetadataName("DotBoxD.UI.Authoring.IUiComponent");
        var uiBuilder = compilation.GetTypeByMetadataName("DotBoxD.UI.Authoring.UiBuilder");
        if (HasComponentImplementation(type, componentInterface))
        { return RenderKind.Handwritten; }
        for (var current = type; current is not null; current = current.BaseType)
        {
            var methods = current.GetMembers().OfType<IMethodSymbol>().Where(method =>
                IsAccessibleRender(method, type, componentInterface, uiBuilder, compilation)).ToArray();
            if (methods.Any(method => !method.IsAbstract && !method.IsStatic &&
                method.ReturnType.ToDisplayString() == "DotBoxD.UI.Authoring.UiElement"))
            { return RenderKind.Handwritten; }
            if (methods.Length > 0)
            { return RenderKind.Incompatible; }
        }
        return RenderKind.None;
    }

    private static bool HasComponentImplementation(INamedTypeSymbol type, INamedTypeSymbol? componentInterface)
        => componentInterface?.GetMembers("Render").OfType<IMethodSymbol>().Any(contract =>
            type.FindImplementationForInterfaceMember(contract) is IMethodSymbol { IsStatic: false } implementation &&
            implementation.ReturnType.ToDisplayString() == "DotBoxD.UI.Authoring.UiElement") == true;

    private static bool IsAccessibleRender(IMethodSymbol method, INamedTypeSymbol type,
        INamedTypeSymbol? componentInterface, INamedTypeSymbol? uiBuilder, Compilation compilation)
        => (method.Name == "Render" || IsExplicitComponentRender(method, componentInterface)) &&
            IsRenderSignature(method, uiBuilder) && (compilation.IsSymbolAccessibleWithin(method, type) ||
                IsExplicitComponentRender(method, componentInterface));

    private static bool IsRenderSignature(IMethodSymbol method, INamedTypeSymbol? uiBuilder)
        => method.Arity == 0 && method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, uiBuilder);

    private static bool IsExplicitComponentRender(IMethodSymbol method, INamedTypeSymbol? componentInterface) =>
        componentInterface is not null && method.ExplicitInterfaceImplementations.Any(i =>
            SymbolEqualityComparer.Default.Equals(i.ContainingType, componentInterface));

    private static Output Generate(Component component, EquatableArray<RazorFile> files, CancellationToken token)
    {
        var hint = component.FullName.Replace("@", "") + ".UiRazor.g.cs";
        if (component.RenderError is { } renderError)
        { return new Output(hint, component.SourcePath, "", null, renderError); }
        if (component.GenerationDisabled)
        { return new Output(hint, component.SourcePath, "", null, null); }
        if (component.HasRenderProperty)
        {
            return new Output(hint, component.SourcePath, "", null,
                "A component member named Render conflicts with generated Render(UiBuilder). Rename the member or hand-write Render(UiBuilder).");
        }
        if (component.Skip)
        { return new Output(hint, component.SourcePath, "", null, null); }
        if (!component.Valid)
        { return new Output(hint, component.SourcePath, "", null, "Use a top-level, non-generic partial class, or hand-write Render(UiBuilder)."); }
        var matches = files.Where(f => string.Equals(f.Path.Replace('\\', '/'), component.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
            f.Path.Replace('\\', '/').EndsWith("/" + component.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
        { return new Output(hint, component.SourcePath, "", null, "Register exactly one matching .ui.razor file as AdditionalFiles: " + component.Path); }
        var file = matches[0];
        try
        {
            var body = UiRazorDocument.Compile(file.Text, component.Type, component.Compilation, component.EndpointConstants, token);
            var ns = component.Namespace.Length == 0 ? "" : "namespace " + component.Namespace + ";\n";
            var source = "// <auto-generated/>\n#nullable enable\n" + ns + component.Modifiers + " " + component.DeclarationKind + " " + component.Name +
                " : global::DotBoxD.UI.Authoring.IUiComponent\n{\npublic global::DotBoxD.UI.Authoring.UiElement Render(global::DotBoxD.UI.Authoring.UiBuilder builder)\n{\n" + body + "\n}\n}\n";
            return new Output(hint, file.Path, file.Text, source, null);
        }
        catch (NotSupportedException error)
        {
            return new Output(component.FullName.Replace("@", string.Empty) + ".UiRazor.Error.g.cs", file.Path,
                file.Text, ErrorSource(component), error.Message);
        }
    }

    private static string ErrorSource(Component component)
    {
        var ns = component.Namespace.Length == 0 ? "" : "namespace " + component.Namespace + ";\n";
        return "// <auto-generated/>\n#nullable enable\n" + ns + component.Modifiers + " " + component.DeclarationKind + " " +
            component.Name + " : global::DotBoxD.UI.Authoring.IUiComponent\n{\npublic global::DotBoxD.UI.Authoring.UiElement Render(global::DotBoxD.UI.Authoring.UiBuilder builder)\n" +
            "{ throw new global::System.InvalidOperationException(\"Safe Razor generation failed.\"); }\n}\n";
    }

    private sealed record RazorFile(string Path, string Text);
    private sealed record Component(string FullName, string Name, string Namespace, INamedTypeSymbol Type, Compilation Compilation, string Modifiers, string DeclarationKind,
        string Path, string SourcePath, bool GenerationDisabled, bool Skip, bool HasRenderProperty, bool Valid,
        EquatableArray<string> EndpointConstants, string? RenderError);
    private sealed record Output(string Hint, string Path, string FileText, string? Source, string? Error);
    private enum RenderKind { None, Handwritten, Incompatible }
}
