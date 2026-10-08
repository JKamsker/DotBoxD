using System.Collections.Frozen;
using Avalonia.Controls;

namespace DotBoxD.UI.Avalonia;

internal static class UiControlFactory
{
    private static readonly FrozenDictionary<UiPrimitive, Func<Control>> Factories =
        new Dictionary<UiPrimitive, Func<Control>>
        {
            [UiPrimitive.Stack] = static () => new StackPanel(),
            [UiPrimitive.Grid] = static () => new Grid(),
            [UiPrimitive.Border] = static () => new Border(),
            [UiPrimitive.Text] = static () => new TextBlock(),
            [UiPrimitive.Button] = static () => new Button(),
            [UiPrimitive.TextBox] = static () => new TextBox(),
            [UiPrimitive.CheckBox] = static () => new CheckBox(),
            [UiPrimitive.ProgressBar] = static () => new ProgressBar(),
            [UiPrimitive.ScrollViewer] = static () => new ScrollViewer(),
            [UiPrimitive.Items] = static () => new StackPanel(),
            [UiPrimitive.Slider] = static () => new Slider()
        }.ToFrozenDictionary();

    public static Control Create(UiPrimitive primitive) => Factories.TryGetValue(primitive, out var create)
        ? create() : throw new UiValidationException("Unregistered Avalonia UI primitive.");

    public static void Attach(Control parent, Control child)
    {
        switch (parent)
        {
            case Panel panel:
                panel.Children.Add(child);
                break;
            case Border border:
                border.Child = child;
                break;
            case ScrollViewer scroll:
                scroll.Content = child;
                break;
            default:
                throw new UiValidationException("Unsupported UI container.");
        }
    }

    public static void Detach(Control control)
    {
        switch (control)
        {
            case Panel panel:
                panel.Children.Clear();
                break;
            case Border border:
                border.Child = null;
                break;
            case ScrollViewer scroll:
                scroll.Content = null;
                break;
        }
    }
}
