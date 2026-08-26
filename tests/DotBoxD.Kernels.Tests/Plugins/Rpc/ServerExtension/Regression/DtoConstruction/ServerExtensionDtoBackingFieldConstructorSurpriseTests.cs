using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoBackingFieldConstructorSurpriseTests
{
    [Fact]
    public void Direct_extension_reconstructs_dto_constructor_assigned_backing_field()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(BackingFieldDtoSource);
        AssertProfileRead(assembly);
    }

    [Fact]
    public void Direct_extension_reconstructs_dto_split_partial_constructor_backing_field()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(
            [
                SplitProfileSharedSource,
                SplitProfileGetterSource,
                SplitProfileConstructorSource
            ]);

        AssertProfileRead(assembly);
    }

    private static void AssertProfileRead(Assembly assembly)
    {
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.String("hero"),
                KernelRpcValue.Bool(true)
            ])));

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control])!;

        var type = result.GetType();
        Assert.Equal("hero", type.GetProperty("Name")!.GetValue(result));
        Assert.Equal(true, type.GetProperty("PreferName")!.GetValue(result));
    }

    private static object CreateControl(Assembly assembly, byte[] response)
    {
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        return Activator.CreateInstance(controlType, [new RecordingRegistry(response)])!;
    }

    private const string BackingFieldDtoSource = """
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

        public sealed class Profile
        {
            private readonly string _name;

            public Profile(string name, bool preferName)
            {
                _name = name;
                PreferName = preferName;
            }

            public string Name => _name;
            public bool PreferName { get; }
        }

        [ServerExtension(typeof(IRemoteControl), "profile")]
        public sealed partial class ProfileKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public Profile Read(HookContext ctx)
            {
                return new Profile("server", true);
            }
        }

        public static class Probe
        {
            public static Profile Read(RemoteControl control) => control.Read();
        }
        """;

    private const string SplitProfileSharedSource = """
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

        [ServerExtension(typeof(IRemoteControl), "profile")]
        public sealed partial class ProfileKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public Profile Read(HookContext ctx)
            {
                return new Profile("server", true);
            }
        }

        public static class Probe
        {
            public static Profile Read(RemoteControl control) => control.Read();
        }
        """;

    private const string SplitProfileConstructorSource = """
        namespace Sample;

        public sealed partial class Profile
        {
            private readonly string _name;

            public Profile(string name, bool preferName)
            {
                _name = name;
                PreferName = preferName;
            }

            public bool PreferName { get; }
        }
        """;

    private const string SplitProfileGetterSource = """
        namespace Sample;

        public sealed partial class Profile
        {
            public string Name => _name;
        }
        """;

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
