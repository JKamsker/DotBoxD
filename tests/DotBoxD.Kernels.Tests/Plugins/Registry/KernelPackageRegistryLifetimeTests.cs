using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins.Registry;

public sealed class KernelPackageRegistryLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_removal_supports_live_contexts_and_preserves_other_contexts(bool byContext)
    {
        var context = new AssemblyLoadContext("Kernel cleanup fixture", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(CachedConventionKernel).Assembly.Location);
            var kernel = assembly.GetType(typeof(CachedConventionKernel).FullName!)!;
            var original = KernelPackageRegistry.Resolve(kernel);
            var shared = KernelPackageRegistry.Resolve<CachedConventionKernel>();

            Assert.Equal(1, byContext
                ? KernelPackageRegistry.UnregisterLoadContext(context)
                : KernelPackageRegistry.Unregister(kernel));
            Assert.Equal(0, KernelPackageRegistry.UnregisterLoadContext(context));
            Assert.NotSame(original, KernelPackageRegistry.Resolve(kernel));
            Assert.Same(shared, KernelPackageRegistry.Resolve<CachedConventionKernel>());
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Explicit_removal_rejects_null_inputs()
    {
        Assert.Throws<ArgumentNullException>(() => KernelPackageRegistry.Unregister(null!));
        Assert.Throws<ArgumentNullException>(() => KernelPackageRegistry.UnregisterLoadContext(null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Package_factories_do_not_retain_collectible_contexts(bool explicitlyRegistered)
    {
        var reference = ResolveAndUnload(explicitlyRegistered);
        for (var i = 0; i < 100 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(10);
        }

        Assert.False(reference.IsAlive, "Kernel package factories must follow collectible type lifetime.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ResolveAndUnload(bool explicitlyRegistered)
    {
        var context = new AssemblyLoadContext("Collectible kernel fixture", isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(typeof(CachedConventionKernel).Assembly.Location);
        var kernel = assembly.GetType(typeof(CachedConventionKernel).FullName!)!;
        if (explicitlyRegistered)
        {
            var create = assembly.GetType(typeof(CachedConventionPluginPackage).FullName!)!.GetMethod("Create")!;
            KernelPackageRegistry.Register(kernel, create.CreateDelegate<Func<DotBoxD.Plugins.PluginPackage>>());
        }

        var package = KernelPackageRegistry.Resolve(kernel);
        Assert.Equal("cached-convention", package.Manifest.PluginId);
        if (!explicitlyRegistered)
            Assert.Same(package, KernelPackageRegistry.Resolve(kernel));
        var reference = new WeakReference(context);
        context.Unload();
        return reference;
    }
}
