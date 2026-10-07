using System.Collections.Immutable;
using Avalonia.Controls;

namespace DotBoxD.UI.Avalonia;

internal sealed class UiKeyedRows(StackPanel panel)
{
    private readonly Dictionary<string, TextBlock> _rows = new(StringComparer.Ordinal);

    public void Update(ImmutableArray<UiListItem> items)
    {
        var keys = items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _rows.Keys.Where(k => !keys.Contains(k)).ToArray())
        {
            panel.Children.Remove(_rows[key]);
            _rows.Remove(key);
        }

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (!_rows.TryGetValue(item.Key, out var row))
            { row = new TextBlock(); _rows.Add(item.Key, row); }
            row.Text = item.Text;
            if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], row))
            { continue; }
            panel.Children.Remove(row);
            panel.Children.Insert(index, row);
        }
    }

    public void Clear()
    {
        panel.Children.Clear();
        _rows.Clear();
    }
}
