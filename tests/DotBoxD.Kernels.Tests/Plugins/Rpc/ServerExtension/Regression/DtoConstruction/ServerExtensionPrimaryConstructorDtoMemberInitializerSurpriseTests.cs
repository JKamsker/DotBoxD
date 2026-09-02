using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionPrimaryConstructorDtoMemberInitializerSurpriseTests
{
    [Fact]
    public void Direct_extension_reconstructs_dto_with_primary_constructor_member_initializer()
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
            public interface IRemoteControl;

            public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
            {
                public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public sealed class Profile(string name)
            {
                public string Name { get; } = name;
            }

            [ServerExtension(typeof(IRemoteControl), "profile-primary-constructor")]
            public sealed partial class ProfileKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public Profile Read(HookContext ctx) => new Profile("server");
            }

            public static class Probe
            {
                public static Profile Read(RemoteControl control) => control.Read();
            }
            """);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record([KernelRpcValue.String("hero")])));

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control])!;

        Assert.Equal("hero", profile.GetType().GetProperty("Name")!.GetValue(profile));
    }

    private static object CreateControl(Assembly assembly, byte[] response)
    {
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        return Activator.CreateInstance(controlType, [new RecordingRegistry(response)])!;
    }

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => "profile-primary-constructor";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("profile-primary-constructor", pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
