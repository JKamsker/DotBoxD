using System.Text.Json;
using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components.Rendering;

namespace DotBoxD.UI.Blazor.Tests.Resources.Validation;

public sealed class UiExtensionRegistryValidationTests
{
    [Fact]
    public void Null_schema_entries_fail_with_argument_validation()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new UiExtensionRegistry<IUiBlazorExtension>(new IUiBlazorExtension[] { null! }));

        Assert.Contains("schemas", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_schema_entries_are_registered()
    {
        var registry = new UiExtensionRegistry<IUiBlazorExtension>([new Badge()]);

        Assert.Contains("test.badge", registry.Schemas);
    }

    private sealed class Badge : IUiBlazorExtension
    {
        public string SchemaId => "test.badge";

        public void Validate(JsonElement payload)
        {
        }

        public void Render(RenderTreeBuilder builder, JsonElement payload)
        {
        }
    }
}
