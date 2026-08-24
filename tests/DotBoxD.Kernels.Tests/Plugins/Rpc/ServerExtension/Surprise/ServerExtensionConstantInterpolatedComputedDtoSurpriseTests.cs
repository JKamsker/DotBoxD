using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionConstantInterpolatedComputedDtoSurpriseTests
{
    private const string Source = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteWorldControl
        {
        }

        public sealed class RemoteWorldControl : IRemoteWorldControl, IServerExtensionClientAccessor
        {
            public RemoteWorldControl(IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public sealed class Profile
        {
            public int Health { get; init; }
            public string Label => $"Health";
        }

        [ServerExtension(typeof(IRemoteWorldControl), "constant-interpolated-profile")]
        public sealed partial class ProfileKernel
        {
            [ServerExtensionMethod(typeof(IRemoteWorldControl))]
            public Profile Read(int health, HookContext ctx)
                => new() { Health = health };
        }

        public static class Probe
        {
            public static Profile Read(RemoteWorldControl control, int health) => control.Read(health);
        }
        """;

    [Fact]
    public void Direct_extension_reconstructs_an_initializer_dto_with_a_constant_interpolated_computed_member()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(Source);
        var control = CreateControl(
            assembly,
            "constant-interpolated-profile",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(3),
                KernelRpcValue.String("Health")
            ])));

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 3])!;

        Assert.Equal("Health", profile.GetType().GetProperty("Label")!.GetValue(profile));
    }

    private static object CreateControl(Assembly assembly, string expectedPluginId, byte[] response)
    {
        var controlType = assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!;
        return Activator.CreateInstance(controlType, [new RecordingRegistry(expectedPluginId, response)])!;
    }

    private sealed class RecordingRegistry(string expectedPluginId, byte[] response)
        : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => expectedPluginId;

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedPluginId, pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
