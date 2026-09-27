using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class GeneratedAssemblyCatalogLifetimeTests
{
    [Theory]
    [InlineData("Catalog")]
    [InlineData("MissingService")]
    [InlineData("ProxySink")]
    [InlineData("GeneratedSink")]
    public async Task Missing_generated_services_do_not_retain_collectible_assemblies(string operation)
        => await AssertCollected(ProbeMissing(operation));

    [Theory]
    [InlineData("PublishedEmpty")]
    [InlineData("Published")]
    [InlineData("Legacy")]
    [InlineData("ProxySink")]
    [InlineData("GeneratedSink")]
    public async Task Completed_catalog_operations_release_unused_collectible_assemblies(string operation)
        => await AssertCollected(ProbePresent(operation));

    [Fact]
    public async Task Unqueried_service_types_are_collectible()
        => await AssertCollected(CreateWithoutQuery());

    [Fact]
    public async Task Live_service_types_preserve_and_replace_their_assembly_catalog_snapshot()
    {
        var lease = CreateLease();
        Collect();
        VerifyAndReplace(lease);
        await AssertCollected(lease.Catalog);
        lease.Owner.Value = null;
        await AssertCollected(lease.Type);
    }

    [Fact]
    public void Explicit_global_registrations_keep_their_service_factories_alive()
    {
        var reference = RegisterGlobally();
        Collect();
        var service = Assert.IsAssignableFrom<Type>(reference.Target);
        var metadata = GeneratedServiceRegistry.GetService(service);
        var proxy = GeneratedServiceRegistry.CreateProxy(service, new RecordingInvoker());

        Assert.Equal(metadata.ProxyType, proxy.GetType());
        Assert.True(service.IsInstanceOfType(proxy));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ProbeMissing(string operation)
    {
        var fixture = CollectibleServiceCatalog.Create(generated: false);
        switch (operation)
        {
            case "Catalog":
                Assert.Empty(GeneratedServiceRegistry.GetServices(fixture.Assembly));
                break;
            case "MissingService":
                Assert.Throws<InvalidOperationException>(() => GeneratedServiceRegistry.GetService(fixture.Service));
                break;
            case "ProxySink":
                var proxies = new RecordingServiceSink();
                GeneratedServiceRegistry.RegisterServices([fixture.Assembly], proxies);
                Assert.Empty(proxies.ServiceTypes);
                break;
            case "GeneratedSink":
                var generated = new RecordingGeneratedSink();
                GeneratedServiceRegistry.RegisterGeneratedServices([fixture.Assembly], generated);
                Assert.Empty(generated.ServiceTypes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        return new WeakReference(fixture.Service);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ProbePresent(string operation)
    {
        var fixture = CollectibleServiceCatalog.Create(generated: operation is "Legacy" or "ProxySink" or "GeneratedSink");
        switch (operation)
        {
            case "PublishedEmpty":
                GeneratedServiceRegistry.RegisterServices(fixture.Assembly, []);
                Assert.Empty(GeneratedServiceRegistry.GetServices(fixture.Assembly));
                break;
            case "Published":
                GeneratedServiceRegistry.RegisterServices(fixture.Assembly, [fixture.Metadata]);
                Assert.Equal(fixture.Service, Assert.Single(GeneratedServiceRegistry.GetServices(fixture.Assembly)).ServiceType);
                break;
            case "Legacy":
                Assert.Equal(fixture.Service, Assert.Single(GeneratedServiceRegistry.GetServices(fixture.Assembly)).ServiceType);
                break;
            case "ProxySink":
                var proxies = new RecordingServiceSink();
                GeneratedServiceRegistry.RegisterServices([fixture.Assembly], proxies);
                Assert.Equal([fixture.Service], proxies.ServiceTypes);
                break;
            case "GeneratedSink":
                var generated = new RecordingGeneratedSink();
                GeneratedServiceRegistry.RegisterGeneratedServices([fixture.Assembly], generated);
                Assert.Equal([fixture.Service], generated.ServiceTypes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        return new WeakReference(fixture.Service);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWithoutQuery() => new(CollectibleServiceCatalog.Create(generated: true).Service);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Lease CreateLease()
    {
        var fixture = CollectibleServiceCatalog.Create(generated: true);
        var catalog = GeneratedServiceRegistry.GetServices(fixture.Assembly);
        return new Lease(new WeakReference(fixture.Service), new WeakReference(catalog), new StrongBox<Type?>(fixture.Service));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyAndReplace(Lease lease)
    {
        var assembly = lease.Owner.Value!.Assembly;
        var catalog = GeneratedServiceRegistry.GetServices(assembly);
        Assert.Same(lease.Catalog.Target, catalog);
        Assert.Equal(lease.Type.Target, Assert.Single(catalog).ServiceType);

        GeneratedServiceRegistry.RegisterServices(assembly, []);

        Assert.Empty(GeneratedServiceRegistry.GetServices(assembly));
        Assert.Single(catalog);
        GC.KeepAlive(assembly);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterGlobally()
    {
        var fixture = CollectibleServiceCatalog.Create(generated: false);
        typeof(GeneratedAssemblyCatalogLifetimeTests).GetMethod(nameof(RegisterTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(fixture.Service).Invoke(null, [fixture.Proxy, fixture.Metadata]);
        return new WeakReference(fixture.Service);
    }

    private static void RegisterTyped<TService>(Type proxy, GeneratedService metadata) where TService : class
        => GeneratedServiceRegistry.Register<TService>(_ => (TService)Activator.CreateInstance(proxy)!,
            _ => new CollectibleServiceCatalog.Dispatcher(), metadata);

    private static async Task AssertCollected(WeakReference reference)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            Collect();
            if (!reference.IsAlive)
            {
                return;
            }

            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        Assert.False(reference.IsAlive, "An assembly lookup cache must not retain an otherwise unused collectible assembly.");
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record Lease(WeakReference Type, WeakReference Catalog, StrongBox<Type?> Owner);
}
