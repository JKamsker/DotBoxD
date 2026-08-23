using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IComputedVirtualDispatchService
{
    int Read(int value);
}

public sealed class ServerExtensionComputedVirtualDispatchSurpriseTests
{
    private const string Source = """
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public abstract record AbstractBaseDto(int Value)
        {
            public int Computed => Calculate();

            protected abstract int Calculate();
        }

        public sealed record AbstractDerivedDto(int Value) : AbstractBaseDto(Value)
        {
            protected override int Calculate() => Value + 2;
        }

        public record BaseDto(int Value)
        {
            protected virtual int Calculate() => Value + 1;
        }

        public record MiddleDto(int Value) : BaseDto(Value)
        {
            public int Computed => Calculate();

            protected override int Calculate() => Value + 2;
        }

        public sealed record DerivedDto(int Value) : MiddleDto(Value)
        {
            protected override int Calculate() => Value + 3;
        }

        [ServerExtension("computed-virtual-dispatch")]
        public sealed partial class ComputedVirtualDispatchKernel
        {
            public int Read(int value, HookContext ctx)
                => (new AbstractDerivedDto(value).Computed * 10) + new DerivedDto(value).Computed;
        }
        """;

    [Fact]
    public async Task Computed_dto_helper_preserves_virtual_override_dispatch()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ComputedVirtualDispatchPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IComputedVirtualDispatchService>(kernel);

        Assert.Equal(56, service.Read(3));
    }
}
