using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class GeneratedAssemblyCatalogAllocationTests
{
    [Fact]
    public void Warm_catalog_reads_remain_allocation_free()
    {
        var fixture = CollectibleServiceCatalog.Create(generated: true);
        var assembly = fixture.Assembly;
        var catalog = GeneratedServiceRegistry.GetServices(assembly);
        for (var i = 0; i < 1000; i++)
        {
            _ = GeneratedServiceRegistry.GetServices(assembly);
        }

        var count = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            count += GeneratedServiceRegistry.GetServices(assembly).Count;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1000, count);
        Assert.Equal(0, allocated);
        Assert.Same(catalog, GeneratedServiceRegistry.GetServices(assembly));
    }
}
