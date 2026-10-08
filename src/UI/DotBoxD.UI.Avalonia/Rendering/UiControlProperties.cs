using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace DotBoxD.UI.Avalonia;

internal static class UiControlProperties
{
    public static void Apply(Control control, UiPropertyId property, UiValue value)
    {
        switch (property)
        {
            case UiPropertyId.Enabled:
                control.IsEnabled = value.Boolean;
                break;
            case UiPropertyId.Visible:
                control.IsVisible = value.Boolean;
                break;
            case UiPropertyId.Text:
                SetText(control, value.Text);
                break;
            case UiPropertyId.Checked:
                ((CheckBox)control).IsChecked = value.Boolean;
                break;
            case UiPropertyId.Value:
                ((RangeBase)control).Value = value.Number;
                break;
            case UiPropertyId.Maximum:
                ((RangeBase)control).Maximum = value.Number;
                break;
            default:
                ApplyLayout(control, property, value);
                break;
        }
    }

    private static void ApplyLayout(Control control, UiPropertyId property, UiValue value)
    {
        switch (property)
        {
            case UiPropertyId.Horizontal:
                ((StackPanel)control).Orientation = value.Boolean ? Orientation.Horizontal : Orientation.Vertical;
                break;
            case UiPropertyId.Spacing:
                ((StackPanel)control).Spacing = value.Number;
                break;
            case UiPropertyId.Columns:
                SetColumns((Grid)control, value.Integer);
                break;
            case UiPropertyId.Row:
                Grid.SetRow(control, value.Integer);
                break;
            case UiPropertyId.Column:
                Grid.SetColumn(control, value.Integer);
                break;
            case UiPropertyId.Padding:
                ((Border)control).Padding = new Thickness(value.Number);
                break;
            default:
                throw new UiValidationException("Unsupported Avalonia property.");
        }
    }

    private static void SetColumns(Grid grid, int columns)
    {
        grid.ColumnDefinitions.Clear();
        for (var i = 0; i < columns; i++)
        { grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); }
    }

    public static void RefreshRows(Grid grid)
    {
        var count = grid.Children.Select(Grid.GetRow).DefaultIfEmpty(0).Max() + 1;
        while (grid.RowDefinitions.Count > count)
        { grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1); }
        while (grid.RowDefinitions.Count < count)
        { grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
    }

    private static void SetText(Control control, string text)
    {
        switch (control)
        {
            case TextBlock block:
                block.Text = text;
                break;
            case TextBox box:
                box.Text = text;
                break;
            case ContentControl content:
                content.Content = text;
                break;
            default:
                throw new UiValidationException("Unsupported text target.");
        }
    }
}
