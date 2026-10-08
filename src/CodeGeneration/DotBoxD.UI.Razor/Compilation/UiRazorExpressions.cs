using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.UI.Razor;

internal sealed class UiRazorExpressions
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resources = new(StringComparer.Ordinal);

    public string State(string declaration)
    {
        var syntax = SyntaxFactory.ParseStatement(declaration) as LocalDeclarationStatementSyntax;
        if (syntax is null || syntax.ContainsDiagnostics || syntax.Declaration.Variables.Count != 1)
        { throw new NotSupportedException("Use @state int|bool|double|string|items name = literal;"); }
        var type = syntax.Declaration.Type.ToString();
        var variable = syntax.Declaration.Variables[0];
        var initial = variable.Initializer?.Value;
        var kind = StateType(type);
        var value = StateValue(type, initial);
        var name = Identifier(variable.Identifier.ValueText);
        Add(name, kind);
        _states.Add(name);
        return "var " + name + " = builder.State(" + value + ");";
    }

    private static string StateType(string type) => type switch
    {
        "int" => "int",
        "bool" => "bool",
        "double" => "double",
        "string" => "string",
        "items" => "global::System.Collections.Immutable.ImmutableArray<global::DotBoxD.UI.UiListItem>",
        _ => throw new NotSupportedException("State uses int, bool, double, string or items; arbitrary CLR state cannot cross the UI boundary.")
    };

    private static string StateValue(string type, ExpressionSyntax? initial) =>
        type == "items" && initial is CollectionExpressionSyntax { Elements.Count: 0 }
            ? "global::System.Collections.Immutable.ImmutableArray<global::DotBoxD.UI.UiListItem>.Empty"
            : Literal(initial?.ToString() ?? "", type);

    public string Kernel(string declaration)
    {
        var parts = Assignment(declaration);
        var call = Call(parts.Value, "Bind");
        if (call.Count is < 1 or > 2)
        { throw new NotSupportedException("Use @kernel name = Ui.Bind(LocalMethod[, state]); with a [UiLocalHandler] method."); }
        var method = QualifiedName(call[0]);
        if (call.Count == 2 && !_states.Contains(call[1]))
        { throw new NotSupportedException("Kernel inputs must name a declared typed state slot."); }
        Add(parts.Name, "kernel");
        return "var " + parts.Name + " = builder.Kernel(" + method + "UiKernel()" +
            (call.Count == 2 ? ", " + call[1] : "") + ");";
    }

    public string Resource(string declaration)
    {
        var parts = Assignment(declaration);
        Add(parts.Name, "resource");
        _resources.Add(parts.Name);
        return "var " + parts.Name + " = builder.Resource(" + Literal(parts.Value, "string") + ");";
    }

    public string Property(string text, string type)
    {
        if (!text.StartsWith("@", StringComparison.Ordinal))
        { return "global::DotBoxD.UI.Authoring.UiLiteral." + LiteralFactory(type) + "(" + AttributeLiteral(text, type) + ")"; }
        var expression = text.Substring(1);
        var input = expression.StartsWith("Ui.TwoWay(", StringComparison.Ordinal);
        var arguments = Call(expression, input ? "TwoWay" : "Bind");
        if (arguments.Count != 1 || !_values.ContainsKey(arguments[0]) || _resources.Contains(arguments[0]))
        { throw new NotSupportedException("Bindings name one declared state or kernel: @Ui.Bind(name) or @Ui.TwoWay(state)."); }
        var name = arguments[0];
        if (input && !_states.Contains(name))
        { throw new NotSupportedException("Two-way inputs require a typed state slot, not a kernel."); }
        return input ? "global::DotBoxD.UI.Authoring.UiBinding<" + type + ">.Input(" + name + ")"
            : "((global::DotBoxD.UI.Authoring.UiBinding<" + type + ">)" + name + ")";
    }

    public string Image(string text)
    {
        var name = text.TrimStart('@');
        if (!_resources.Contains(name))
        { throw new NotSupportedException("Image Resource must name an @resource declaration; URLs and filesystem paths are forbidden."); }
        return "global::DotBoxD.UI.Authoring.UiLiteral.Integer(" + name + ")";
    }

    public string Event(string element, string text)
    {
        if (!text.StartsWith("@Ui.", StringComparison.Ordinal))
        { throw new NotSupportedException("OnClick requires @Ui.Handle(kernel, state) or @Ui.Remote(endpoint)."); }
        if (text.StartsWith("@Ui.Remote(", StringComparison.Ordinal))
        {
            var call = Call(text.Substring(1), "Remote");
            if (call.Count != 1)
            { throw new NotSupportedException("Remote routes require one positive endpoint ID or generated endpoint constant."); }
            var endpoint = int.TryParse(call[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0 ? call[0] : QualifiedName(call[0]);
            return "builder.Remote(" + element + ", " + endpoint + ")";
        }
        var local = Call(text.Substring(1), "Handle");
        if (local.Count != 2 || !_values.TryGetValue(local[0], out var type) || type != "kernel" || !_states.Contains(local[1]))
        { throw new NotSupportedException("Local events use @Ui.Handle(declaredKernel, outputState); lower methods with [UiLocalHandler]."); }
        return "builder.Handle(" + element + ", " + local[0] + ", " + local[1] + ")";
    }

    public string Composition(string name, string arguments)
    {
        var values = arguments.Length == 0 ? Array.Empty<string>() : arguments.Split(',').Select(v => v.Trim()).ToArray();
        if (values.Any(v => !_values.ContainsKey(v)))
        { throw new NotSupportedException("Composition arguments must name declared state, kernel or resource handles."); }
        return "((global::DotBoxD.UI.Authoring.IUiComponent)new " + QualifiedName(name) + "(" + string.Join(", ", values) + ")).Render(builder)";
    }

    private void Add(string name, string type)
    {
        if (name == "builder" || _values.ContainsKey(name))
        { throw new NotSupportedException("State, kernel and resource names must be unique and cannot be 'builder'."); }
        _values.Add(name, type);
    }

    private static (string Name, string Value) Assignment(string declaration)
    {
        var equal = declaration.IndexOf('=');
        if (equal <= 0 || !declaration.EndsWith(";", StringComparison.Ordinal))
        { throw new NotSupportedException("Declarations use name = value; on a separate line."); }
        return (Identifier(declaration.Substring(0, equal).Trim()), declaration.Substring(equal + 1).Trim().TrimEnd(';').Trim());
    }

    private static string Identifier(string name) => SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
        ? name : throw new NotSupportedException("Use ordinary C# identifiers for declared UI values.");

    private static string QualifiedName(string name)
        => string.Join(".", name.Split('.').Select(Identifier));

    private static IReadOnlyList<string> Call(string text, string method)
    {
        var syntax = SyntaxFactory.ParseExpression(text) as InvocationExpressionSyntax;
        if (syntax is null || syntax.ContainsDiagnostics || syntax.Expression.ToString() != "Ui." + method ||
            syntax.ArgumentList.Arguments.Any(a => a.NameColon is not null || !a.RefKindKeyword.IsKind(SyntaxKind.None)))
        { throw new NotSupportedException("Use the closed Ui.Bind/TwoWay/Handle/Remote syntax; arbitrary expressions, delegates and services are forbidden."); }
        return syntax.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToArray();
    }

    private static string Literal(string text, string type)
    {
        var syntax = SyntaxFactory.ParseExpression(text);
        var value = syntax is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.UnaryMinusExpression)
            ? prefix.Operand : syntax;
        if (syntax.ContainsDiagnostics || value is not LiteralExpressionSyntax literal || !Matches(literal, type))
        { throw new NotSupportedException("Initial state and literal properties require finite typed literals; use a restricted kernel for computed values."); }
        return type == "double" ? "(double)(" + text + ")" : text;
    }

    private static bool Matches(LiteralExpressionSyntax literal, string type) => type switch
    {
        "string" => literal.IsKind(SyntaxKind.StringLiteralExpression),
        "bool" => literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression),
        "int" => literal.Token.Value is int,
        "double" => literal.Token.Value is int or double,
        _ => false
    };

    private static string AttributeLiteral(string text, string type)
        => type == "string" ? SymbolDisplay.FormatLiteral(text, quote: true) : Literal(text, type);

    private static string LiteralFactory(string type) => type switch
    { "string" => "Text", "int" => "Integer", "bool" => "Boolean", "double" => "Number", _ => throw new NotSupportedException("Items require a typed state binding.") };
}
