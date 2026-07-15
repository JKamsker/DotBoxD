using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientResponseHelperCollisionTests
{
    [Fact]
    public void Generated_payload_helpers_do_not_collide_with_service_methods()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(PayloadHelperCollisionSource);
        var generated = string.Join(
            Environment.NewLine,
            PluginAnalyzerGeneratedPackageFactory.GeneratedSources(PayloadHelperCollisionSource));

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("private static class KernelRpcResponseReader", generated, StringComparison.Ordinal);
        Assert.Contains(
            "global::System.DateOnly ReadKernelRpcPayload0(int dayNumber)",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeFromPayloadWireOffset", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_response_container_avoids_service_member_names()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(ContainerCollisionSource);
        var generated = string.Join(
            Environment.NewLine,
            PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ContainerCollisionSource));

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("private static class KernelRpcResponseReader1", generated, StringComparison.Ordinal);
        Assert.Contains("KernelRpcResponseReader1.Read0(__response)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_direct_response_container_avoids_extension_method_names()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(DirectContainerCollisionSource);
        var generated = string.Join(
            Environment.NewLine,
            PluginAnalyzerGeneratedPackageFactory.GeneratedSources(DirectContainerCollisionSource));

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("private static class KernelRpcResponseReader1", generated, StringComparison.Ordinal);
        Assert.Contains("KernelRpcResponseReader1.Read0(__response)", generated, StringComparison.Ordinal);
    }

    private const string PayloadHelperCollisionSource = """
        using System;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;

        namespace Sample;

        public interface IDateService
        {
            DateOnly ReadKernelRpcPayload0(int dayNumber);
        }

        public interface IDateHost
        {
            [HostBinding("host.date.from-day-number", "date.read", SandboxEffect.Cpu)]
            DateOnly FromDayNumber(int dayNumber);
        }

        [ServerExtension("date-collision", typeof(IDateService))]
        public sealed partial class DateKernel
        {
            public DateOnly ReadKernelRpcPayload0(int dayNumber, HookContext ctx)
            {
                return ctx.Host<IDateHost>().FromDayNumber(dayNumber);
            }
        }
        """;

    private const string ContainerCollisionSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;

        namespace Sample;

        public interface IValueServiceBase
        {
            int KernelRpcResponseReader(int value);
        }

        public interface IValueService : IValueServiceBase;

        [ServerExtension("container-collision", typeof(IValueService))]
        public sealed partial class ValueKernel
        {
            public int KernelRpcResponseReader(int value, HookContext ctx) => value;
        }
        """;

    private const string DirectContainerCollisionSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public IServerExtensionClientRegistry ServerExtensions { get; } = null!;
        }

        [ServerExtension(typeof(IRemoteControl), "direct-container-collision")]
        public sealed partial class ValueKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public int KernelRpcResponseReader(HookContext ctx) => 1;
        }

        public static class Probe
        {
            public static int Read(RemoteControl control) => control.KernelRpcResponseReader();
        }
        """;
}
