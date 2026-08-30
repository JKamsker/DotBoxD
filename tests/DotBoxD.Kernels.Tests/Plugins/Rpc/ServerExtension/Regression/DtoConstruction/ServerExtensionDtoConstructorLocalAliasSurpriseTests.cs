using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoConstructorLocalAliasSurpriseTests
{
    [Fact]
    public void Direct_extension_reconstructs_a_dto_constructor_with_a_parameter_local_alias()
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
            public interface IRemoteWorldControl;

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
                    var exactName = name;
                    Name = exactName;
                }

                public string Name { get; }
            }

            [ServerExtension(typeof(IRemoteWorldControl), "profile")]
            public sealed partial class ProfileKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public Profile Read(HookContext ctx) => new("hero");
            }

            public static class Probe
            {
                public static Profile Read(RemoteWorldControl control) => control.Read();
            }
            """);
        var controlType = assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!;
        var control = Activator.CreateInstance(
            controlType,
            [new RecordingRegistry(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record([KernelRpcValue.String("hero")])))])!;

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control])!;

        Assert.Equal("hero", profile.GetType().GetProperty("Name")!.GetValue(profile));
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
