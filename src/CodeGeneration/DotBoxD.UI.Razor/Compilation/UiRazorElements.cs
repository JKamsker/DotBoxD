using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Xml.Linq;

namespace DotBoxD.UI.Razor;

internal static class UiRazorElements
{
    private static readonly IReadOnlyDictionary<string, string> Primitives = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["UiStack"] = "Stack",
        ["UiVertical"] = "Stack",
        ["UiHorizontal"] = "Stack",
        ["UiWhen"] = "Stack",
        ["UiGrid"] = "Grid",
        ["UiBorder"] = "Border",
        ["UiScrollViewer"] = "ScrollViewer",
        ["UiText"] = "Text",
        ["UiButton"] = "Button",
        ["UiTextBox"] = "TextBox",
        ["UiCheckBox"] = "CheckBox",
        ["UiSlider"] = "Slider",
        ["UiProgressBar"] = "ProgressBar",
        ["UiItems"] = "Items",
        ["UiImage"] = "Image"
    };
    private static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Text"] = "string",
        ["Enabled"] = "bool",
        ["Visible"] = "bool",
        ["Checked"] = "bool",
        ["Horizontal"] = "bool",
        ["Value"] = "double",
        ["Maximum"] = "double",
        ["Spacing"] = "double",
        ["Padding"] = "double",
        ["Columns"] = "int",
        ["Row"] = "int",
        ["Column"] = "int",
        ["Resource"] = "int",
        ["Items"] = "global::System.Collections.Immutable.ImmutableArray<global::DotBoxD.UI.UiListItem>"
    };

    public static string Render(XElement node, UiRazorExpressions expressions, INamedTypeSymbol component, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateNode(node);
        var tag = node.Name.LocalName;
        if (tag == "UiComponent")
        { return Composition(node, expressions, component); }
        if (!Primitives.TryGetValue(tag, out var primitive))
        { throw new NotSupportedException("Unknown tag '" + tag + "'. Use a Ui primitive or UiComponent with an IUiComponent worker-side composition."); }
        var (properties, click) = Properties(node, primitive, expressions);
        var children = node.Elements().ToArray();
        var maximum = MaximumChildren(primitive);
        if (children.Length > maximum)
        { throw new NotSupportedException("Unsupported children on " + tag + "; UiItems uses bounded keyed state, not arbitrary foreach templates."); }
        var renderedChildren = children.Select(c => Render(c, expressions, component, token)).ToArray();
        var element = primitive == "Grid" ? Grid(node, properties, renderedChildren)
            : "builder.Element(global::DotBoxD.UI.UiPrimitive." + primitive + ", [" + string.Join(", ", properties) + "]" +
                (renderedChildren.Length == 0 ? "" : ", " + string.Join(", ", renderedChildren)) + ")";
        return click is null ? element : expressions.Event(element, click);
    }

    private static string Grid(XElement node, List<string> properties, string[] children)
    {
        var columns = node.Attribute("Columns")?.Value ?? "1";
        if (columns.StartsWith("@", StringComparison.Ordinal))
        {
            if (node.Elements().Any(c => c.Attribute("Row") is null || c.Attribute("Column") is null))
            { throw new NotSupportedException("Bound grid columns require explicit Row and Column on each child. Use literal Columns for automatic placement, or hand-write UiBuilder composition."); }
            return "builder.Element(global::DotBoxD.UI.UiPrimitive.Grid, [" + string.Join(", ", properties) + "]" + Arguments(children) + ")";
        }
        if (SyntaxFactory.ParseExpression(columns) is not LiteralExpressionSyntax { Token.Value: int count } || count is < 1 or > 64)
        { throw new NotSupportedException("Grid Columns must be an integer literal from 1 to 64, or a binding with explicit child positions."); }
        return "builder.Configure(builder.Grid(" + columns + Arguments(children) + "), [" + string.Join(", ", properties) + "])";
    }

    private static string Arguments(string[] children) => children.Length == 0 ? "" : ", " + string.Join(", ", children);

    private static int MaximumChildren(string primitive) => primitive switch
    { "Stack" or "Grid" => 1_000, "Border" or "ScrollViewer" => 1, _ => 0 };

    private static string Composition(XElement node, UiRazorExpressions expressions, INamedTypeSymbol component)
    {
        if (node.HasElements || node.Attributes().Any(a => a.Name != "Type" && a.Name != "Arguments") || node.Attribute("Type") is null)
        { throw new NotSupportedException("Use <UiComponent Type=\"WorkerComponent\" Arguments=\"state, kernel\" /> with no children."); }
        return expressions.Composition(node.Attribute("Type")!.Value, node.Attribute("Arguments")?.Value ?? "", component);
    }

    private static readonly IReadOnlyDictionary<string, string[]> PropertyPrimitives = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Text"] = ["Text", "Button", "TextBox", "CheckBox", "Image"],
        ["Checked"] = ["CheckBox"],
        ["Value"] = ["Slider", "ProgressBar"],
        ["Maximum"] = ["Slider", "ProgressBar"],
        ["Horizontal"] = ["Stack"],
        ["Spacing"] = ["Stack"],
        ["Padding"] = ["Border"],
        ["Columns"] = ["Grid"],
        ["Items"] = ["Items"],
        ["Resource"] = ["Image"]
    };

    private static bool Allowed(string primitive, string property) => property is "Enabled" or "Visible" or "Row" or "Column" ||
        PropertyPrimitives.TryGetValue(property, out var primitives) && primitives.Contains(primitive, StringComparer.Ordinal);

    private static void ValidateNode(XElement node)
    {
        if (node.Name.NamespaceName.Length != 0 || node.Nodes().Any(n => n is not XElement &&
            (n is not XText content || !string.IsNullOrWhiteSpace(content.Value))))
        { throw new NotSupportedException("Use DotBoxD primitives and Text attributes; raw markup, text fragments and XML namespaces are forbidden."); }
    }

    private static (List<string> Properties, string? Click) Properties(XElement node, string primitive, UiRazorExpressions expressions)
    {
        var properties = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        string? click = null;
        if (node.Name.LocalName == "UiHorizontal")
        {
            names.Add("Horizontal");
            properties.Add("global::DotBoxD.UI.Authoring.UiLiteral.Boolean(true).Property(global::DotBoxD.UI.UiPropertyId.Horizontal)");
        }
        foreach (var attribute in node.Attributes())
        {
            if (attribute.Name.LocalName == "OnClick" && primitive == "Button")
            { click = attribute.Value; continue; }
            if (attribute.Name.LocalName == "Key" && primitive == "Items" && attribute.Value == "Key")
            { continue; }
            properties.Add(Property(attribute, node.Name.LocalName, primitive, names, expressions));
        }
        ValidateLiteralRange(node, primitive);
        if (primitive == "Image" && !names.Contains("Resource"))
        { throw new NotSupportedException("UiImage requires a Resource from an @resource host-granted handle declaration."); }
        return (properties, click);
    }

    private static void ValidateLiteralRange(XElement node, string primitive)
    {
        if (primitive is not ("Slider" or "ProgressBar"))
        { return; }
        var value = LiteralNumber(node.Attribute("Value"));
        var maximum = LiteralNumber(node.Attribute("Maximum"));
        if (value is not null && maximum is not null && value > maximum)
        { throw new NotSupportedException("Value cannot exceed Maximum when both are literal UI values."); }
    }

    private static double? LiteralNumber(XAttribute? attribute)
    {
        if (attribute is null || attribute.Value.StartsWith("@", StringComparison.Ordinal))
        { return null; }
        var syntax = SyntaxFactory.ParseExpression(attribute.Value);
        var prefix = syntax as PrefixUnaryExpressionSyntax;
        var negative = prefix?.IsKind(SyntaxKind.UnaryMinusExpression) == true;
        var value = negative ? prefix!.Operand : syntax;
        return !syntax.ContainsDiagnostics && value is LiteralExpressionSyntax { Token.Value: int integer }
            ? negative ? -integer : integer
            : value is LiteralExpressionSyntax { Token.Value: double number } && !double.IsNaN(number) && !double.IsInfinity(number)
                ? negative ? -number : number
                : null;
    }

    private static string Property(XAttribute attribute, string tag, string primitive, HashSet<string> names, UiRazorExpressions expressions)
    {
        var name = tag == "UiWhen" && attribute.Name.LocalName == "Condition" ? "Visible" : attribute.Name.LocalName;
        if (attribute.Name.NamespaceName.Length != 0 || attribute.IsNamespaceDeclaration)
        { throw new NotSupportedException("Attribute namespaces and splatting are forbidden; use explicit semantic properties."); }
        if (!names.Add(name) || !Types.TryGetValue(name, out var type) || !Allowed(primitive, name))
        { throw new NotSupportedException("Unsupported or duplicate attribute '" + name + "' on " + tag + ". Use semantic properties; DOM events, CSS, JS, HTML and services are forbidden."); }
        ValidateInput(primitive, name, attribute.Value);
        var value = name == "Resource" ? expressions.Image(attribute.Value) : expressions.Property(attribute.Value, type);
        return value + ".Property(global::DotBoxD.UI.UiPropertyId." + name + ")";
    }

    private static void ValidateInput(string primitive, string property, string value)
    {
        if (value.StartsWith("@Ui.TwoWay(", StringComparison.Ordinal) &&
            !((primitive == "TextBox" && property == "Text") || (primitive == "CheckBox" && property == "Checked") ||
              (primitive == "Slider" && property == "Value")))
        { throw new NotSupportedException("TwoWay is supported only on TextBox.Text, CheckBox.Checked and Slider.Value."); }
    }
}
