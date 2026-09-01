using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoConstructorTupleAssignmentSurpriseTests
{
    [Fact]
    public void Direct_extension_reconstructs_dto_with_tuple_assigned_read_only_properties()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [RpcService]
            public interface IRemoteProfiles;

            public sealed class RemoteProfiles : IRemoteProfiles, IServerExtensionClientAccessor
            {
                public RemoteProfiles(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public sealed class Profile
            {
                public Profile(string name, int level) => (Name, Level) = (name, level);

                public string Name { get; }
                public int Level { get; }
            }

            [ServerExtension(typeof(IRemoteProfiles), "profile-tuple")]
            public sealed partial class ProfileKernel
            {
                [ServerExtensionMethod(typeof(IRemoteProfiles))]
                public Profile Read(int id, HookContext ctx) => new("ignored", id);
            }

            public static class Probe
            {
                public static Profile Read(RemoteProfiles profiles, int id) => profiles.Read(id);
            }
            """);
        var profiles = CreateProfiles(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.String("Ada"),
                KernelRpcValue.Int32(7)
            ])));

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [profiles, 42])!;

        var type = profile.GetType();
        Assert.Equal("Ada", type.GetProperty("Name")!.GetValue(profile));
        Assert.Equal(7, type.GetProperty("Level")!.GetValue(profile));
    }

    private static object CreateProfiles(Assembly assembly, byte[] response)
    {
        var profilesType = assembly.GetType("Sample.RemoteProfiles", throwOnError: true)!;
        return Activator.CreateInstance(profilesType, [new RecordingRegistry(response)])!;
    }

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => "profile-tuple";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("profile-tuple", pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
