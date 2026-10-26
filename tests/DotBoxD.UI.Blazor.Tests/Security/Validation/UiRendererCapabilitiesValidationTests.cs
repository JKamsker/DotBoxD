using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests.Security.Validation;

public sealed class UiRendererCapabilitiesValidationTests
{
    [Fact]
    public void Null_package_node_fails_closed_with_ui_validation_exception()
    {
        var package = UiFixture.TextPackage() with { Nodes = [null!] };

        Assert.Throws<UiValidationException>(() => UiRendererCapabilities.Core.Validate(package));
    }

    [Fact]
    public void Valid_text_package_remains_supported()
    {
        UiRendererCapabilities.Core.Validate(UiFixture.TextPackage());
    }
}
