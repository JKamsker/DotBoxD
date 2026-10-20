using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia;

internal sealed class UiControlInput(UiInputQueue queue)
{
    private readonly List<(Control Control, EventHandler<AvaloniaPropertyChangedEventArgs> Handler)> _properties = [];
    private readonly List<(Button Button, EventHandler<RoutedEventArgs> Handler)> _buttons = [];
    public bool Applying { get; set; }

    public void Attach(UiNode node, Control control, IEnumerable<UiEvent> events)
    {
        foreach (var route in events.Where(e => e.NodeId == node.Id))
        {
            var button = (Button)control;
            EventHandler<RoutedEventArgs> handler = (_, _) => { if (!Applying) { queue.Write(new UiInput(EventId: route.Id)); } };
            button.Click += handler;
            _buttons.Add((button, handler));
        }
        foreach (var property in node.Properties.Where(p => p.TwoWay))
        {
            EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, change) =>
            {
                if (!Applying && Read(control, property.Id, change.Property) is { } value)
                { queue.Write(new UiInput(NodeId: node.Id, PropertyId: property.Id, Value: value)); }
            };
            control.PropertyChanged += handler;
            _properties.Add((control, handler));
        }
    }

    private static UiValue? Read(Control control, UiPropertyId property, AvaloniaProperty changed)
        => property switch
        {
            UiPropertyId.Text when changed == TextBox.TextProperty => UiValue.FromString(((TextBox)control).Text ?? ""),
            UiPropertyId.Checked when changed == ToggleButton.IsCheckedProperty => UiValue.FromBoolean(((CheckBox)control).IsChecked == true),
            UiPropertyId.Value when changed == RangeBase.ValueProperty => UiValue.FromNumber(((Slider)control).Value),
            _ => null
        };

    public void Clear()
    {
        foreach (var (control, handler) in _properties)
        { control.PropertyChanged -= handler; }
        foreach (var (button, handler) in _buttons)
        { button.Click -= handler; }
        _properties.Clear();
        _buttons.Clear();
    }
}
