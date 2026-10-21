using System.Text.Json;
using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components.Rendering;

namespace DotBoxD.UI.Blazor;

/// <summary>Audited host implementation of a stable, bounded schema. Plugins only supply data.</summary>
public interface IUiBlazorExtension : IUiExtensionSchema
{
    void Render(RenderTreeBuilder builder, JsonElement payload);
}
