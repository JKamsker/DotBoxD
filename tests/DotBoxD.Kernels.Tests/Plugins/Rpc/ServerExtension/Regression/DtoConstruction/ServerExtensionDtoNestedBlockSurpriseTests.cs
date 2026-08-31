using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoNestedBlockSurpriseTests
{
    [Fact]
    public void Direct_extension_reconstructs_dto_with_unconditional_nested_constructor_assignment()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
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
                public Profile(string name)
                {
                    {
                        Name = name;
                    }
                }

                public string Name { get; }
            }

            [ServerExtension(typeof(IRemoteWorldControl), "profile-nested-block")]
            public sealed partial class ProfileKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public Profile Read(int id, HookContext ctx) => new("hero");
            }

            public static class Probe
            {
                public static Profile Read(RemoteWorldControl control, int id) => control.Read(id);
            }
            """);
        var control = Activator.CreateInstance(
            assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!,
            [new RecordingRegistry(
                "profile-nested-block",
                KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record([KernelRpcValue.String("hero")])))])!;

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 7])!;

        Assert.Equal("hero", profile.GetType().GetProperty("Name")!.GetValue(profile));
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
