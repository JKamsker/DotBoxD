using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoConstructorIdentityCastSurpriseTests
{
    [Fact]
    public void Direct_extension_reconstructs_a_dto_constructor_identity_cast_assignment()
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
            public interface IRemoteWorldControl
            {
            }

            public sealed class RemoteWorldControl : IRemoteWorldControl, IServerExtensionClientAccessor
            {
                public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public sealed class Profile
            {
                public Profile(string name)
                {
                    Name = (string)name;
                }

                public string Name { get; }
            }

            [ServerExtension(typeof(IRemoteWorldControl), "profile")]
            public sealed partial class ProfileKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public Profile Read(string name, HookContext ctx)
                {
                    return new Profile(name);
                }
            }

            public static class Probe
            {
                public static Profile Read(RemoteWorldControl control, string name) => control.Read(name);
            }
            """);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record([KernelRpcValue.String("hero")])));

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, "hero"])!;

        Assert.Equal("hero", profile.GetType().GetProperty("Name")!.GetValue(profile));
    }

    private static object CreateControl(Assembly assembly, byte[] response)
    {
        var controlType = assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!;
        return Activator.CreateInstance(controlType, [new RecordingRegistry(response)])!;
    }

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => "profile";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("profile", pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
