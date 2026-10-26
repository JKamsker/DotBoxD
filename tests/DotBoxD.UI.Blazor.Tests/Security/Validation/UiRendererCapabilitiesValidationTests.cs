using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests.Security.Validation;

public sealed class UiRendererCapabilitiesValidationTests
{
    [Fact]
    public void Null_package_extension_fails_closed_with_ui_validation_exception()
    {
        var package = UiFixture.TextPackage() with { Extensions = [null!] };

        Assert.Throws<UiValidationException>(() => UiRendererCapabilities.Core.Validate(package));
    }

    [Fact]
    public void Package_without_extensions_remains_valid()
    {
        var package = UiFixture.TextPackage();

        UiRendererCapabilities.Core.Validate(package);
    }
}
