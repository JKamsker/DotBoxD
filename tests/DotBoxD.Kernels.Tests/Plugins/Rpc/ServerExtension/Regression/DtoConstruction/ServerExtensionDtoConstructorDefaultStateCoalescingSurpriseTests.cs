using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoConstructorDefaultStateCoalescingSurpriseTests
{
    [Fact]
    public void Direct_extension_generated_client_reconstructs_dto_with_default_state_coalescing_constructor()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            #nullable enable

            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [RpcService]
            public interface IRemoteWorldControl;

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
                    Name = name ?? Name;
                }

                public string Name { get; }
            }

            [ServerExtension(typeof(IRemoteWorldControl), "profile-default-state")]
            public sealed partial class ProfileKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public Profile Read(HookContext ctx) => new("host");
            }

            public static class Probe
            {
                public static Profile Read(RemoteWorldControl control) => control.Read();
            }
            """);
        var controlType = assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!;
        var control = Activator.CreateInstance(
            controlType,
            [new RecordingRegistry(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record([
                KernelRpcValue.String("hero")])))])!;

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control])!;

        Assert.Equal("hero", profile.GetType().GetProperty("Name")!.GetValue(profile));
    }

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => "profile-default-state";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("profile-default-state", pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
