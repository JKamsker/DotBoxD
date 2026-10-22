using System.Reflection;
using DotBoxD.Services.Tests.Coverage.Core;
using Xunit;

namespace DotBoxD.Services.Tests.Lifetime.Collectible;

public sealed class GeneratedServiceUnregisterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removal_invalidates_existing_registration_versions_and_allows_explicit_reregistration(bool byAssembly)
    {
        var fixture = CollectibleServiceCatalog.Create(generated: false);
        Register(fixture);
        var proxy = GeneratedServiceRegistry.CreateProxy(fixture.Service, new RecordingInvoker(), out var version);
        Assert.True(GeneratedServiceRegistry.IsRegistrationCurrent(fixture.Service, version, out var oldRegistryVersion));

        Assert.Equal(1, byAssembly
            ? GeneratedServiceRegistry.UnregisterAssembly(fixture.Assembly)
            : GeneratedServiceRegistry.Unregister(fixture.Service));
        Assert.False(GeneratedServiceRegistry.IsRegistrationCurrent(fixture.Service, version, out var newRegistryVersion));
        Assert.True(newRegistryVersion > oldRegistryVersion);
        Assert.True(fixture.Service.IsInstanceOfType(proxy));
        Assert.Throws<InvalidOperationException>(() => GeneratedServiceRegistry.GetService(fixture.Service));
        Assert.Equal(0, GeneratedServiceRegistry.Unregister(fixture.Service));
        Assert.Equal(newRegistryVersion, GeneratedServiceRegistry.CurrentRegistrationVersion);

        Register(fixture);
        Assert.False(GeneratedServiceRegistry.IsRegistrationCurrent(fixture.Service, version, out _));
        _ = GeneratedServiceRegistry.CreateProxy(fixture.Service, new RecordingInvoker(), out var replacementVersion);
        Assert.True(GeneratedServiceRegistry.IsRegistrationCurrent(fixture.Service, replacementVersion, out _));
        Assert.NotEqual(version, replacementVersion);
    }

    [Fact]
    public void Unregister_rejects_null_inputs()
    {
        Assert.Throws<ArgumentNullException>(() => GeneratedServiceRegistry.Unregister(null!));
        Assert.Throws<ArgumentNullException>(() => GeneratedServiceRegistry.UnregisterAssembly(null!));
    }

    [Fact]
    public void Warm_service_registry_reads_remain_allocation_free()
    {
        var fixture = CollectibleServiceCatalog.Create(generated: false);
        Register(fixture);
        for (var i = 0; i < 1000; i++)
            _ = GeneratedServiceRegistry.GetService(fixture.Service);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            _ = GeneratedServiceRegistry.GetService(fixture.Service);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        GC.KeepAlive(fixture);
    }

    private static void Register(CollectibleServiceCatalog.Fixture fixture) =>
        typeof(GeneratedServiceUnregisterTests).GetMethod(nameof(RegisterTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(fixture.Service).Invoke(null, [fixture]);

    private static void RegisterTyped<TService>(CollectibleServiceCatalog.Fixture fixture) where TService : class =>
        GeneratedServiceRegistry.Register<TService>(_ => (TService)Activator.CreateInstance(fixture.Proxy)!,
            _ => new CollectibleServiceCatalog.Dispatcher(), fixture.Metadata);
}
