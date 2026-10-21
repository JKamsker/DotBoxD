using System.Collections.Frozen;
using System.Globalization;
using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace DotBoxD.UI.Blazor;

internal static class UiHtmlTree
{
    private static readonly FrozenDictionary<UiPrimitive, string> Tags = new Dictionary<UiPrimitive, string>
    {
        [UiPrimitive.Extension] = "div",
        [UiPrimitive.Image] = "img",
        [UiPrimitive.Text] = "span",
        [UiPrimitive.Button] = "button",
        [UiPrimitive.TextBox] = "input",
        [UiPrimitive.Slider] = "input",
        [UiPrimitive.CheckBox] = "label",
        [UiPrimitive.ProgressBar] = "progress",
        [UiPrimitive.Stack] = "div",
        [UiPrimitive.Grid] = "div",
        [UiPrimitive.Border] = "div",
        [UiPrimitive.ScrollViewer] = "div",
        [UiPrimitive.Items] = "div"
    }.ToFrozenDictionary();
    public static void Render(RenderTreeBuilder builder, BlazorUiSnapshot snapshot, int nodeId,
        object receiver, Func<UiInput, Task> submit, bool enabled = true)
    {
        var node = snapshot.Nodes[nodeId];
        enabled &= snapshot.Get(nodeId, UiPropertyId.Enabled)?.Boolean ?? true;
        builder.OpenRegion(0);
        builder.OpenElement(1, Tag(node.Primitive));
        builder.SetKey(nodeId);
        builder.AddAttribute(2, "data-dotboxd-node", nodeId);
        builder.AddAttribute(3, "class", "dotboxd-" + node.Primitive.ToString().ToLowerInvariant());
        builder.AddAttribute(4, "hidden", snapshot.Get(nodeId, UiPropertyId.Visible)?.Boolean == false);
        builder.AddAttribute(5, "style", UiHtmlStyle.Create(snapshot, node));
        RenderContent(builder, snapshot, node, receiver, submit, enabled);
        foreach (var child in node.Children)
        { Render(builder, snapshot, child, receiver, submit, enabled); }
        builder.CloseElement();
        builder.CloseRegion();
    }

    private static void RenderContent(RenderTreeBuilder builder, BlazorUiSnapshot snapshot, UiNode node,
        object receiver, Func<UiInput, Task> submit, bool enabled)
    {
        var nodeId = node.Id;
        switch (node.Primitive)
        {
            case UiPrimitive.Extension:
                var extension = snapshot.Extensions[nodeId];
                builder.OpenRegion(22);
                extension.Schema.Render(builder, extension.Payload);
                builder.CloseRegion();
                break;
            case UiPrimitive.Image:
                var image = snapshot.Images[snapshot.Get(nodeId, UiPropertyId.Resource)!.Integer];
                builder.AddAttribute(18, "src", image.Source);
                builder.AddAttribute(19, "alt", snapshot.Get(nodeId, UiPropertyId.Text)?.Text ?? "");
                builder.AddAttribute(20, "width", image.Width);
                builder.AddAttribute(21, "height", image.Height);
                break;
            default:
                RenderCoreContent(builder, snapshot, node, receiver, submit, enabled);
                break;
        }
    }

    private static void RenderCoreContent(RenderTreeBuilder builder, BlazorUiSnapshot snapshot, UiNode node,
        object receiver, Func<UiInput, Task> submit, bool enabled)
    {
        var nodeId = node.Id;
        switch (node.Primitive)
        {
            case UiPrimitive.Button:
                builder.AddAttribute(6, "type", "button");
                builder.AddAttribute(7, "disabled", !enabled);
                if (snapshot.Package.Events.SingleOrDefault(e => e.NodeId == nodeId) is { } route)
                {
                    builder.AddAttribute(8, "onclick", EventCallback.Factory.Create<MouseEventArgs>(receiver,
                        () => submit(new UiInput(EventId: route.Id))));
                }
                builder.AddContent(9, snapshot.Get(nodeId, UiPropertyId.Text)?.Text);
                break;
            case UiPrimitive.TextBox:
                Input(builder, snapshot, node, receiver, submit, enabled, "text", UiPropertyId.Text);
                break;
            case UiPrimitive.Slider:
                Input(builder, snapshot, node, receiver, submit, enabled, "range", UiPropertyId.Value);
                break;
            case UiPrimitive.CheckBox:
                builder.OpenElement(10, "input");
                Input(builder, snapshot, node, receiver, submit, enabled, "checkbox", UiPropertyId.Checked);
                builder.CloseElement();
                builder.AddContent(11, snapshot.Get(nodeId, UiPropertyId.Text)?.Text);
                break;
            case UiPrimitive.ProgressBar:
                builder.AddAttribute(12, "value", Number(snapshot.Get(nodeId, UiPropertyId.Value)?.Number ?? 0));
                builder.AddAttribute(13, "max", Number(snapshot.Get(nodeId, UiPropertyId.Maximum)?.Number ?? 100));
                break;
            case UiPrimitive.Text:
                builder.AddContent(14, snapshot.Get(nodeId, UiPropertyId.Text)?.Text);
                break;
            case UiPrimitive.Items:
                foreach (var item in snapshot.Get(nodeId, UiPropertyId.Items)?.Items ?? [])
                {
                    builder.OpenElement(15, "span");
                    builder.SetKey(item.Key);
                    builder.AddAttribute(16, "data-dotboxd-key", item.Key);
                    builder.AddContent(17, item.Text);
                    builder.CloseElement();
                }
                break;
        }
    }

    private static void Input(RenderTreeBuilder builder, BlazorUiSnapshot snapshot, UiNode node,
        object receiver, Func<UiInput, Task> submit, bool enabled, string type, UiPropertyId property)
    {
        var twoWay = node.Properties.Any(p => p.Id == property && p.TwoWay);
        builder.AddAttribute(0, "type", type);
        builder.AddAttribute(1, "disabled", !enabled || !twoWay);
        if (property == UiPropertyId.Checked)
        { builder.AddAttribute(2, "checked", snapshot.Get(node.Id, property)?.Boolean == true); }
        else
        {
            builder.AddAttribute(3, "value", property == UiPropertyId.Text
                ? snapshot.Get(node.Id, property)?.Text ?? "" : Number(snapshot.Get(node.Id, property)?.Number ?? 0));
        }
        if (property == UiPropertyId.Value)
        {
            builder.AddAttribute(4, "min", "0");
            builder.AddAttribute(5, "max", Number(snapshot.Get(node.Id, UiPropertyId.Maximum)?.Number ?? 100));
            builder.AddAttribute(6, "step", "any");
        }
        if (twoWay)
        {
            builder.AddAttribute(7, property == UiPropertyId.Checked ? "onchange" : "oninput",
                EventCallback.Factory.Create<ChangeEventArgs>(receiver, args =>
                    submit(new UiInput(NodeId: node.Id, PropertyId: property, Value: Parse(property, args.Value)))));
            builder.SetUpdatesAttributeName(property == UiPropertyId.Checked ? "checked" : "value");
        }
    }

    private static UiValue Parse(UiPropertyId property, object? value) => property switch
    {
        UiPropertyId.Text when value is string text => UiValue.FromString(text),
        UiPropertyId.Checked when value is bool check => UiValue.FromBoolean(check),
        UiPropertyId.Value when value is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            => UiValue.FromNumber(number),
        _ => new UiValue((UiValueKind)0)
    };

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Tag(UiPrimitive primitive) => Tags.TryGetValue(primitive, out var tag)
        ? tag : throw new UiValidationException("Unregistered Blazor UI primitive.");
}
