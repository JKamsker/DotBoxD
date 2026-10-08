using System.Text.Json;
using Avalonia.Controls;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia;

/// <summary>Audited host implementation of a stable, bounded schema. Plugins only supply data.</summary>
public interface IUiAvaloniaExtension : IUiExtensionSchema
{
    Control Create(JsonElement payload);
}
