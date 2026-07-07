using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginPackageRootContractTests
{
    public static TheoryData<string, Func<PluginPackage, PluginPackage>> InvalidRootPackages()
        => new()
        {
            { "Manifest", package => new PluginPackage(null!, package.Module, package.Entrypoints) },
            { "Manifest", package => package with { Manifest = null! } },
            { "Module", package => new PluginPackage(package.Manifest, null!, package.Entrypoints) },
            { "Module", package => package with { Module = null! } },
            { "Entrypoints", package => new PluginPackage(package.Manifest, package.Module, null!) },
            { "Entrypoints", package => package with { Entrypoints = null! } },
        };

    [Theory]
    [MemberData(nameof(InvalidRootPackages))]
    public void GetRequiredCapabilities_rejects_packages_with_null_roots(
        string parameterName,
        Func<PluginPackage, PluginPackage> mutate)
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var invalid = mutate(FireDamagePluginPackage.Create());

        var exception = Assert.ThrowsAny<ArgumentException>(() => server.GetRequiredCapabilities(invalid));

        Assert.IsNotType<NullReferenceException>(exception);
        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidRootPackages))]
    public async Task InstallAsync_rejects_packages_with_null_roots(
        string parameterName,
        Func<PluginPackage, PluginPackage> mutate)
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var invalid = mutate(FireDamagePluginPackage.Create());

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.IsNotType<NullReferenceException>(exception);
        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidRootPackages))]
    public async Task InstallPoolAsync_rejects_packages_with_null_roots(
        string parameterName,
        Func<PluginPackage, PluginPackage> mutate)
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var invalid = mutate(FireDamagePluginPackage.Create());

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await server.InstallPoolAsync(invalid, degreeOfParallelism: 1).AsTask());

        Assert.IsNotType<NullReferenceException>(exception);
        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidRootPackages))]
    public async Task InstallServerExtensionAsync_rejects_packages_with_null_roots(
        string parameterName,
        Func<PluginPackage, PluginPackage> mutate)
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var invalid = mutate(FireDamagePluginPackage.Create());

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await server.InstallServerExtensionAsync(invalid).AsTask());

        Assert.IsNotType<NullReferenceException>(exception);
        Assert.Equal(parameterName, exception.ParamName);
    }
}
