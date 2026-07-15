using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientDirectPayloadDecodeTests
{
    [Fact]
    public void Both_generated_client_forms_use_synchronous_direct_response_helpers()
    {
        var generated = string.Join(
            Environment.NewLine,
            [
                .. PluginAnalyzerGeneratedPackageFactory.GeneratedSources(DirectClientSource),
                .. PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ProxyClientSource)
            ]);

        Assert.Contains("class PayloadKernelServerExtensionClient", generated, StringComparison.Ordinal);
        Assert.Contains("class PayloadKernelDirectServerExtensionClientExtensions", generated, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(generated, "validator.SkipValue();"));
        Assert.Equal(2, CountOccurrences(generated, "reader.EnsureConsumed();"));
        Assert.DoesNotContain("KernelRpcBinaryCodec.DecodeValue(__response)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Async_generated_client_with_direct_response_helper_compiles_as_CSharp12()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(
            ProxyClientSource,
            LanguageVersion.CSharp12);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generated_client_validates_trailing_bytes_before_invoking_a_DTO_constructor()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectClientSource);
        var valid = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(
            [KernelRpcValue.Record([KernelRpcValue.Int32(7)])]));
        var response = new byte[valid.Length + 1];
        valid.CopyTo(response, 0);
        response[^1] = (byte)KernelRpcValueKind.Unit;
        var control = CreateControl(assembly, "payload", response);
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var ex = Assert.Throws<TargetInvocationException>(() =>
            probe.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [control]));

        Assert.IsType<FormatException>(ex.InnerException);
        var resultType = assembly.GetType("Sample.PayloadResult", throwOnError: true)!;
        Assert.Equal(0, resultType.GetProperty("ConstructorCalls", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
    }

    [Fact]
    public void Generated_client_rejects_a_wrong_typed_kind_with_FormatException()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(WrongKindSource);
        var control = CreateControl(
            assembly,
            "wrong-kind",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.String("not-an-int")));
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var ex = Assert.Throws<TargetInvocationException>(() =>
            probe.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [control]));

        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public void Generated_client_nullable_false_ignores_the_placeholder_value_type()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(NullableSource);
        var control = CreateControl(
            assembly,
            "nullable",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
                [KernelRpcValue.Bool(false), KernelRpcValue.String("ignored")])));
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var result = probe.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control]);

        Assert.Null(result);
    }

    private static object CreateControl(Assembly assembly, string pluginId, byte[] response)
    {
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        return Activator.CreateInstance(controlType, [new RecordingRegistry(pluginId, response)])!;
    }

    private static int CountOccurrences(string text, string value)
        => text.Split([value], StringSplitOptions.None).Length - 1;

    private const string DirectClientSource = """
        using System.Collections.Generic;
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

        public sealed class PayloadResult
        {
            public PayloadResult(int value)
            {
                ConstructorCalls++;
                Value = value;
            }

            public static int ConstructorCalls { get; private set; }
            public int Value { get; }
        }

        [ServerExtension(typeof(IRemoteControl), "payload")]
        public sealed partial class PayloadKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public List<PayloadResult> Read(HookContext ctx) => new();
        }

        public static class Probe
        {
            public static List<PayloadResult> Read(RemoteControl control) => control.Read();
        }
        """;

    private const string ProxyClientSource = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        public interface IPayloadService
        {
            ValueTask<List<int>> ReadAsync();
        }

        [ServerExtension("payload", typeof(IPayloadService))]
        public sealed partial class PayloadKernel
        {
            public List<int> Read(HookContext ctx) => new();
        }
        """;

    private const string WrongKindSource = """
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
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(IRemoteControl), "wrong-kind")]
        public sealed partial class WrongKindKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public int Read(HookContext ctx) => 0;
        }

        public static class Probe
        {
            public static int Read(RemoteControl control) => control.Read();
        }
        """;

    private const string NullableSource = """
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
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(IRemoteControl), "nullable")]
        public sealed partial class NullableKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public int? Read(int? value, HookContext ctx) => value;
        }

        public static class Probe
        {
            public static int? Read(RemoteControl control) => control.Read(null);
        }
        """;

    private sealed class RecordingRegistry(string expectedPluginId, byte[] response)
        : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => expectedPluginId;

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedPluginId, pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
