using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionPrimaryConstructorCapturedGetterSurpriseTests
{
    [Fact]
    public void Direct_extension_reconstructs_dto_with_primary_constructor_captured_getter()
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
            public interface IRemoteProfiles;

            public sealed class RemoteProfiles : IRemoteProfiles, IServerExtensionClientAccessor
            {
                public RemoteProfiles(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public sealed class Profile(string name)
            {
                public string Name => name;
            }

            [ServerExtension(typeof(IRemoteProfiles), "profile-captured-getter")]
            public sealed partial class ProfileKernel
            {
                [ServerExtensionMethod(typeof(IRemoteProfiles))]
                public Profile Read(int id, HookContext ctx) => new("ignored");
            }

            public static class Probe
            {
                public static Profile Read(RemoteProfiles profiles, int id) => profiles.Read(id);
            }
            """);
        var profiles = Activator.CreateInstance(
            assembly.GetType("Sample.RemoteProfiles", throwOnError: true)!,
            [new RecordingRegistry(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record([KernelRpcValue.String("hero")])))])!;

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [profiles, 42])!;

        Assert.Equal("hero", profile.GetType().GetProperty("Name")!.GetValue(profile));
    }

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => "profile-captured-getter";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("profile-captured-getter", pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
