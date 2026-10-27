using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests.Security;

public sealed class UiRendererCapabilitiesDefaultCollectionTests
{
    public static IEnumerable<object[]> DefaultCollections()
    {
        yield return [UiFixture.TextPackage() with { Extensions = default }];
        yield return [UiFixture.TextPackage() with { Nodes = default }];
        yield return [UiFixture.TextPackage() with { RequiredFeatures = default }];
    }

    [Theory]
    [MemberData(nameof(DefaultCollections))]
    public void Core_capabilities_reject_default_package_collections(UiPackage package)
        => Assert.Throws<UiValidationException>(() => UiRendererCapabilities.Core.Validate(package));

    [Fact]
    public void Core_capabilities_accept_initialized_empty_package_collections()
        => UiRendererCapabilities.Core.Validate(UiFixture.TextPackage() with
        {
            Extensions = [],
            RequiredFeatures = []
        });
}
