using System.Globalization;

namespace DotBoxD.UI.Blazor;

internal static class UiHtmlStyle
{
    // Every token is host-selected or a validated bounded number. No plugin CSS strings or URLs.
    public static string Create(BlazorUiSnapshot snapshot, UiNode node)
    {
        var layout = Layout(snapshot, node);
        if (snapshot.Get(node.Id, UiPropertyId.Row) is { } row)
        { layout += "grid-row:" + (row.Integer + 1).ToString(CultureInfo.InvariantCulture) + ";"; }
        if (snapshot.Get(node.Id, UiPropertyId.Column) is { } column)
        { layout += "grid-column:" + (column.Integer + 1).ToString(CultureInfo.InvariantCulture) + ";"; }
        if (snapshot.Get(node.Id, UiPropertyId.Visible)?.Boolean == false)
        { layout += "display:none;"; }
        return layout;
    }

    private static string Layout(BlazorUiSnapshot snapshot, UiNode node) => node.Primitive switch
    {
        UiPrimitive.Stack => "display:flex;flex-direction:" +
            (snapshot.Get(node.Id, UiPropertyId.Horizontal)?.Boolean == true ? "row" : "column") +
            ";gap:" + Number(snapshot.Get(node.Id, UiPropertyId.Spacing)?.Number ?? 0) + "px;",
        UiPrimitive.Grid => "display:grid;grid-template-columns:repeat(" +
            (snapshot.Get(node.Id, UiPropertyId.Columns)?.Integer ?? 1).ToString(CultureInfo.InvariantCulture) + ",minmax(0,1fr));",
        UiPrimitive.Border => "border:1px solid currentColor;padding:" + Number(snapshot.Get(node.Id, UiPropertyId.Padding)?.Number ?? 0) + "px;",
        UiPrimitive.ScrollViewer => "overflow:auto;max-height:100%;",
        UiPrimitive.Items => "display:flex;flex-direction:column;",
        _ => ""
    };

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
}
