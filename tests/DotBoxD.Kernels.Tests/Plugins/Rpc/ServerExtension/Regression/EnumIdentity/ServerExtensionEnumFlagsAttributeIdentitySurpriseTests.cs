using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionEnumFlagsAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_reader_rejects_lookalike_flags_undeclared_high_bit_combination()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssemblyWithReferences(
            ForeignFlagsSource,
            CompileForeignFlagsAttribute());
        var combinedBits = unchecked((long)((1UL << 63) | 1UL));
        var control = CreateControl(assembly, combinedBits);

        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;
        var fakeFlags = Enum.ToObject(assembly.GetType("Sample.ForeignFlags", throwOnError: true)!, 1UL);

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => probe.GetMethod("Echo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [control, fakeFlags]));

        Assert.IsType<NotSupportedException>(exception.InnerException);
    }

    [Fact]
    public void Generated_reader_accepts_declared_high_bit_combination_for_real_flags_enum()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(RealFlagsSource);
        var combinedBits = unchecked((long)((1UL << 63) | 1UL));
        var result = InvokeEcho(assembly, "RealFlags", "Low", combinedBits);

        Assert.Equal("Low, High", result.ToString());
    }

    [Fact]
    public void Generated_reader_accepts_declared_non_flags_high_bit_value()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(NonFlagsSource);
        var highBit = unchecked((long)(1UL << 63));
        var result = InvokeEcho(assembly, "NonFlags", "High", highBit);

        Assert.Equal("High", result.ToString());
    }

    private static object InvokeEcho(System.Reflection.Assembly assembly, string enumName, string inputName, long response)
    {
        var control = CreateControl(assembly, response);
        var enumType = assembly.GetType("Sample." + enumName, throwOnError: true)!;
        var input = Enum.Parse(enumType, inputName);
        return assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Echo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [control, input])!;
    }

    private static object CreateControl(System.Reflection.Assembly assembly, long response)
        => Activator.CreateInstance(
            assembly.GetType("Sample.RemoteControl", throwOnError: true)!,
            [new RecordingRegistry("enum-identity", KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int64(response)))])!;

    private static MetadataReference CompileForeignFlagsAttribute()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignFlagsAttribute",
            [CSharpSyntaxTree.ParseText("""
                namespace System;

                [AttributeUsage(AttributeTargets.Enum)]
                public sealed class FlagsAttribute : Attribute
                {
                }
                """)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);

        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(System.Collections.Immutable.ImmutableArray.Create("Foreign"));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static reference => MetadataReference.CreateFromFile(reference)) ?? [];

    private const string ForeignFlagsSource = """
        extern alias Foreign;

        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
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

        [Foreign::System.Flags]
        public enum ForeignFlags : ulong
        {
            Low = 1UL,
            High = 1UL << 63
        }

        [ServerExtension(typeof(IRemoteControl), "enum-identity")]
        public sealed partial class EnumKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public ForeignFlags Echo(ForeignFlags value, HookContext ctx) => value;
        }

        public static class Probe
        {
            public static ForeignFlags Echo(RemoteControl control, ForeignFlags value) => control.Echo(value);
        }
        """;

    private static readonly string RealFlagsSource = CommonSourcePrefix + """
        [System.Flags]
        public enum RealFlags : ulong
        {
            Low = 1UL,
            High = 1UL << 63
        }

        """ + CommonSourceSuffix.Replace("ENUM", "RealFlags", StringComparison.Ordinal);

    private static readonly string NonFlagsSource = CommonSourcePrefix + """
        public enum NonFlags : ulong
        {
            Low = 1UL,
            High = 1UL << 63
        }

        """ + CommonSourceSuffix.Replace("ENUM", "NonFlags", StringComparison.Ordinal);

    private const string CommonSourcePrefix = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
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

        """;

    private const string CommonSourceSuffix = """
        [ServerExtension(typeof(IRemoteControl), "enum-identity")]
        public sealed partial class EnumKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public ENUM Echo(ENUM value, HookContext ctx) => value;
        }

        public static class Probe
        {
            public static ENUM Echo(RemoteControl control, ENUM value) => control.Echo(value);
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
