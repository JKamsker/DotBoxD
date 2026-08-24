using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionNullForgivingComputedDtoSurpriseTests
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
            public string Name { get; init; } = "";
            public string Label => Name!;
        }

        [ServerExtension(typeof(IRemoteWorldControl), "null-forgiving-profile")]
        public sealed partial class ProfileKernel
        {
            [ServerExtensionMethod(typeof(IRemoteWorldControl))]
            public Profile Read(HookContext ctx) => new() { Name = "hero" };
        }

        public static class Probe
        {
            public static Profile Read(RemoteWorldControl control) => control.Read();
        }
        """;

    [Fact]
    public void Server_extension_reconstructs_an_initializer_dto_with_a_null_forgiving_computed_member()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(Source);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.String("hero"),
                KernelRpcValue.String("hero")
            ])));

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control])!;

        var type = profile.GetType();
        Assert.Equal("hero", type.GetProperty("Name")!.GetValue(profile));
        Assert.Equal("hero", type.GetProperty("Label")!.GetValue(profile));
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
            => "null-forgiving-profile";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("null-forgiving-profile", pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
