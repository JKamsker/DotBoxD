using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class KernelPackageRegistryRaceTests
{
    [Fact]
    public void Resolve_rejects_a_null_factory_registered_during_convention_lookup()
    {
        using var lookupEntered = new ManualResetEventSlim();
        using var releaseLookup = new ManualResetEventSlim();
        var kernelType = new BlockingAssemblyType(
            typeof(ConcurrentRegistrationKernel),
            lookupEntered,
            releaseLookup);

        var resolution = Task.Run(() => KernelPackageRegistry.Resolve(kernelType));
        try
        {
            Assert.True(lookupEntered.Wait(TimeSpan.FromSeconds(5)));
            KernelPackageRegistry.Register(kernelType, () => null!);

            releaseLookup.Set();

            var exception = Assert.Throws<InvalidOperationException>(
                () => resolution.GetAwaiter().GetResult());

            Assert.Contains(kernelType.FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains("returned null", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            releaseLookup.Set();
        }
    }

    private sealed class BlockingAssemblyType : TypeDelegator
    {
        private readonly Assembly assembly;
        private readonly ManualResetEventSlim lookupEntered;
        private readonly ManualResetEventSlim releaseLookup;

        public BlockingAssemblyType(
            Type type,
            ManualResetEventSlim lookupEntered,
            ManualResetEventSlim releaseLookup) : base(type)
        {
            assembly = type.Assembly;
            this.lookupEntered = lookupEntered;
            this.releaseLookup = releaseLookup;
        }

        public override Assembly Assembly => new BlockingAssembly(assembly, lookupEntered, releaseLookup);
    }

    private sealed class BlockingAssembly(
        Assembly assembly,
        ManualResetEventSlim lookupEntered,
        ManualResetEventSlim releaseLookup) : Assembly
    {
        public override Type? GetType(string name, bool throwOnError, bool ignoreCase)
        {
            lookupEntered.Set();
            Assert.True(releaseLookup.Wait(TimeSpan.FromSeconds(5)));
            return assembly.GetType(name, throwOnError, ignoreCase);
        }
    }
}

public sealed class ConcurrentRegistrationKernel;

public static class ConcurrentRegistrationPluginPackage
{
    public static PluginPackage Create() => KernelPackageRegistry.Resolve<FireDamageKernel>();
}
