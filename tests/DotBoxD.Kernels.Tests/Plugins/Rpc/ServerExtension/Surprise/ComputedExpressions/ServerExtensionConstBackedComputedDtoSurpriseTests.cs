using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IConstBackedComputedDtoService
{
    string ReadLabel(string health);
}

public sealed class ServerExtensionConstBackedComputedDtoSurpriseTests
{
    private const decimal ExpectedScore = 1234567890123456789.012300m;

    private const string LabelSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class Profile
        {
            private const string DefaultLabel = "Health";

            public string Health { get; init; } = string.Empty;
            public string Label => DefaultLabel;
        }

        [ServerExtension("const-backed-computed-dto")]
        public sealed partial class ConstBackedComputedDtoKernel
        {
            public string ReadLabel(string health, HookContext ctx) => new Profile { Health = health }.Label;
        }
        """;

    private const string DecimalSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class Profile
        {
            private const decimal DefaultScore = 1234567890123456789.012300m;

            public string Health { get; init; } = string.Empty;
            public decimal Score => DefaultScore;
        }

        [ServerExtension("const-backed-computed-decimal-dto")]
        public sealed partial class ConstBackedComputedDecimalDtoKernel
        {
            public decimal Read(HookContext ctx) => new Profile { Health = "Critical" }.Score;
        }
        """;

    private const string EnumSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public enum ProfileStatus : ulong
        {
            Critical = ulong.MaxValue
        }

        public sealed class Profile
        {
            private const ProfileStatus DefaultStatus = ProfileStatus.Critical;

            public string Health { get; init; } = string.Empty;
            public ProfileStatus Status => DefaultStatus;
        }

        [ServerExtension("const-backed-computed-enum-dto")]
        public sealed partial class ConstBackedComputedEnumDtoKernel
        {
            public ProfileStatus Read(HookContext ctx) => new Profile { Health = "Critical" }.Status;
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_a_const_backed_computed_member()
    {
        var package = CreatePackage(
            LabelSource,
            "Sample.ConstBackedComputedDtoPluginPackage",
            typeof(IConstBackedComputedDtoService));
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IConstBackedComputedDtoService>(kernel);

        Assert.Equal("Health", service.ReadLabel("Critical"));
    }

    [Fact]
    public async Task Server_extension_preserves_a_const_backed_computed_decimal_member()
    {
        var package = CreatePackage(
            DecimalSource,
            "Sample.ConstBackedComputedDecimalDtoPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(DecimalSandboxValue(ExpectedScore), result);
    }

    [Fact]
    public async Task Server_extension_preserves_a_const_backed_computed_enum_member()
    {
        var package = CreatePackage(
            EnumSource,
            "Sample.ConstBackedComputedEnumDtoPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(SandboxValue.FromInt64(-1), result);
    }

    private static SandboxValue DecimalSandboxValue(decimal value)
        => SandboxValue.FromRecord(decimal.GetBits(value).Select(SandboxValue.FromInt32).ToArray());

    private static PluginPackage CreatePackage(
        string source,
        string factoryTypeName,
        params Type[] additionalReferenceTypes)
        => PluginAnalyzerGeneratedPackageFactory.Create(
            source,
            factoryTypeName,
            additionalReferenceTypes);
}
