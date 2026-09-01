using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionGeneratedDtoReaderRegressionTests
{
    private const string BaseQualifiedComputedDtoSource = """
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

        public class ProfileRoot
        {
            public int Health { get; init; }
            public int Rank { get; init; }

            protected int ComputeScore() => Health + Rank;
        }

        public class ProfileBase : ProfileRoot
        {
            protected new int ComputeScore() => base.ComputeScore();
        }

        public sealed class Profile : ProfileBase
        {
            public int Score => base.ComputeScore();
        }

        [ServerExtension(typeof(IRemoteWorldControl), "base-qualified-profile")]
        public sealed partial class ProfileKernel
        {
            [ServerExtensionMethod(typeof(IRemoteWorldControl))]
            public Profile Read(int x, HookContext ctx)
            {
                return new Profile { Health = x, Rank = 9 };
            }
        }

        public static class Probe
        {
            public static Profile Read(RemoteWorldControl control, int x) => control.Read(x);
        }
        """;

    [Fact]
    public void Direct_extension_reconstructs_a_dto_with_a_base_qualified_helper_computed_member()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(BaseQualifiedComputedDtoSource);
        var control = CreateControl(
            assembly,
            "base-qualified-profile",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(3),
                KernelRpcValue.Int32(9),
                KernelRpcValue.Int32(12)
            ])));

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 3])!;

        var type = profile.GetType();
        Assert.Equal(3, type.GetProperty("Health")!.GetValue(profile));
        Assert.Equal(9, type.GetProperty("Rank")!.GetValue(profile));
        Assert.Equal(12, type.GetProperty("Score")!.GetValue(profile));
    }
}
