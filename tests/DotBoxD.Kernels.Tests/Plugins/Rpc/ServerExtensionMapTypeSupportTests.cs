using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Map-shaped server-extension type support (issue #44): a <c>Dictionary&lt;K,V&gt;</c> round-trips as a
/// parameter, as a return, and as a nested DTO field across the wire <c>Map</c> kind, the runtime
/// marshaller, and the generated client; and a kernel body can read (<c>map.get</c>/<c>map.containsKey</c>)
/// and build (<c>map.empty</c>/<c>map.set</c>) a map server-side. Scalar map keys only — a non-scalar key
/// is rejected at generation time.
/// </summary>
public sealed class ServerExtensionMapTypeSupportTests
{
    [Fact]
    public void Marshaller_round_trips_a_dictionary_through_a_map_value()
    {
        var dictionary = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var sandbox = KernelRpcMarshaller.ToSandboxValue(dictionary, typeof(Dictionary<string, int>));

        var map = Assert.IsType<MapValue>(sandbox);
        Assert.Equal(SandboxType.Map(SandboxType.String, SandboxType.I32), map.Type);
        Assert.Equal(2, map.Values.Count);

        var restored = Assert.IsType<Dictionary<string, int>>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(Dictionary<string, int>)));
        Assert.Equal(dictionary, restored);

        Assert.Equal(
            SandboxType.Map(SandboxType.String, SandboxType.I32),
            KernelRpcMarshaller.SandboxTypeOf(typeof(Dictionary<string, int>)));
    }

    [Fact]
    public void Marshaller_reads_a_map_into_a_readonly_dictionary()
    {
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue> { [SandboxValue.FromInt32(1)] = SandboxValue.FromString("x") },
            SandboxType.I32,
            SandboxType.String);

        var restored = KernelRpcMarshaller.FromSandboxValue(map, typeof(IReadOnlyDictionary<int, string>));

        var dictionary = Assert.IsAssignableFrom<IReadOnlyDictionary<int, string>>(restored);
        Assert.Equal("x", dictionary[1]);
    }

    [Fact]
    public void Converter_round_trips_a_map_value_through_the_wire_kind()
    {
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue> { [SandboxValue.FromString("k")] = SandboxValue.FromInt32(9) },
            SandboxType.String,
            SandboxType.I32);

        var wire = KernelRpcValueConverter.FromSandboxValue(map);

        wire.RequireKind(KernelRpcValueKind.Map);
        var restored = Assert.IsType<MapValue>(
            KernelRpcValueConverter.ToSandboxValue(wire, SandboxType.Map(SandboxType.String, SandboxType.I32)));
        Assert.Equal(map, restored);
    }

    [Fact]
    public void Converter_round_trips_a_record_whose_field_is_a_map()
    {
        var record = SandboxValue.FromRecord(
        [
            SandboxValue.FromInt32(7),
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue> { [SandboxValue.FromString("seed")] = SandboxValue.FromInt32(7) },
                SandboxType.String,
                SandboxType.I32)
        ]);
        var expectedType = SandboxType.Record([SandboxType.I32, SandboxType.Map(SandboxType.String, SandboxType.I32)]);

        var wire = KernelRpcBinaryCodec.DecodeValue(
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValueConverter.FromSandboxValue(record)));
        var restored = KernelRpcValueConverter.ToSandboxValue(wire, expectedType);

        Assert.Equal(record, restored);
    }

    [Fact]
    public void Direct_extension_marshals_a_dictionary_parameter_to_a_map_value()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectMapParameterSource);
        var control = CreateControl(assembly, KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(0)), out var registry);
        var scores = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Sum", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, scores]);

        Assert.Equal(0, result);
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        var map = Assert.Single(arguments);
        map.RequireKind(KernelRpcValueKind.Map);
        Assert.Equal(scores, ReadWireMap(map));
    }

    [Fact]
    public void Direct_extension_reads_a_map_response_into_a_dictionary()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectMapReturnSource);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Map(
            [
                KernelRpcValue.String("a"),
                KernelRpcValue.Int32(1),
                KernelRpcValue.String("b"),
                KernelRpcValue.Int32(2)
            ])),
            out _);

        var snapshot = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Snapshot", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control]);

        var dictionary = Assert.IsAssignableFrom<IReadOnlyDictionary<string, int>>(snapshot);
        Assert.Equal(2, dictionary.Count);
        Assert.Equal(1, dictionary["a"]);
        Assert.Equal(2, dictionary["b"]);
    }

    [Fact]
    public void Direct_extension_record_return_with_map_field_round_trips()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectRecordReturnWithMapFieldSource);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(7),
                KernelRpcValue.Map([KernelRpcValue.String("seed"), KernelRpcValue.Int32(7)])
            ])),
            out _);

        var bag = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Make", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 7])!;

        var type = bag.GetType();
        Assert.Equal(7, type.GetProperty("Id")!.GetValue(bag));
        var scores = Assert.IsAssignableFrom<IReadOnlyDictionary<string, int>>(type.GetProperty("Scores")!.GetValue(bag));
        Assert.Equal(7, scores["seed"]);
    }

    [Fact]
    public async Task A_generated_kernel_reads_and_builds_a_map_server_side()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(MapBodySource, "Sample.ScoreBumpPluginPackage");
        Assert.Equal("Bump", package.Manifest.RpcEntrypoint);

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var scores = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("hero")] = SandboxValue.FromInt32(41)
            },
            SandboxType.String,
            SandboxType.I32);

        var hit = await kernel.InvokeServerExtensionAsync([scores, SandboxValue.FromString("hero")]);

        var bumped = Assert.IsType<MapValue>(hit);
        Assert.Equal(42, Assert.IsType<I32Value>(bumped.Values[SandboxValue.FromString("hero")]).Value);
        Assert.Single(bumped.Values);

        var miss = await kernel.InvokeServerExtensionAsync([scores, SandboxValue.FromString("villain")]);
        Assert.Empty(Assert.IsType<MapValue>(miss).Values);
    }

    [Fact]
    public async Task A_generated_kernel_overwrites_an_existing_map_key_server_side()
    {
        // Exercises map.set's replace branch: the body writes a key, then writes the same key again in the
        // same map (dict[key] = first; dict[key] = second). The build-into-a-fresh-map cases only ever insert.
        var package = PluginAnalyzerGeneratedPackageFactory.Create(MapReplaceBodySource, "Sample.ScoreReplacePluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync(
            [SandboxValue.FromString("hero"), SandboxValue.FromInt32(1), SandboxValue.FromInt32(9)]);

        var map = Assert.IsType<MapValue>(result);
        Assert.Equal(9, Assert.IsType<I32Value>(map.Values[SandboxValue.FromString("hero")]).Value);
        Assert.Single(map.Values);
    }

    [Fact]
    public void Server_extension_rejects_an_unsupported_map_key_type()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("bad-map-key")]
            public sealed partial class BadMapKeyKernel
            {
                public int Use(Dictionary<double, int> scores, HookContext ctx)
                {
                    return 0;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" && d.GetMessage().Contains("map key", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, int> ReadWireMap(KernelRpcValue map)
    {
        var result = new Dictionary<string, int>(map.ItemCount / 2);
        for (var i = 0; i < map.ItemCount; i += 2)
        {
            result[map.GetItem(i).TextValue] = map.GetItem(i + 1).Int32Value;
        }

        return result;
    }

    private static object CreateControl(Assembly assembly, byte[] response, out RecordingRegistry registry)
    {
        var controlType = assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!;
        registry = new RecordingRegistry(response);
        return Activator.CreateInstance(controlType, [registry])!;
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private const string DirectMapParameterSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteWorldControl : IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(RemoteWorldControl), "score-sum")]
        public sealed partial class ScoreSumKernel
        {
            [ServerExtensionMethod(typeof(RemoteWorldControl))]
            public int Sum(Dictionary<string, int> scores, HookContext ctx)
            {
                return 0;
            }
        }

        public static class Probe
        {
            public static int Sum(RemoteWorldControl control, Dictionary<string, int> scores)
                => control.Sum(scores);
        }
        """;

    private const string DirectMapReturnSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteWorldControl : IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(RemoteWorldControl), "score-snapshot")]
        public sealed partial class ScoreSnapshotKernel
        {
            [ServerExtensionMethod(typeof(RemoteWorldControl))]
            public Dictionary<string, int> Snapshot(HookContext ctx)
            {
                return new Dictionary<string, int>();
            }
        }

        public static class Probe
        {
            public static Dictionary<string, int> Snapshot(RemoteWorldControl control)
                => control.Snapshot();
        }
        """;

    private const string DirectRecordReturnWithMapFieldSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteWorldControl : IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public readonly record struct ScoreBag(int Id, Dictionary<string, int> Scores);

        [ServerExtension(typeof(RemoteWorldControl), "score-bag")]
        public sealed partial class ScoreBagKernel
        {
            [ServerExtensionMethod(typeof(RemoteWorldControl))]
            public ScoreBag Make(int id, HookContext ctx)
            {
                var scores = new Dictionary<string, int>();
                scores["seed"] = id;
                return new ScoreBag(id, scores);
            }
        }

        public static class Probe
        {
            public static ScoreBag Make(RemoteWorldControl control, int id) => control.Make(id);
        }
        """;

    private const string MapBodySource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("score-bump")]
        public sealed partial class ScoreBumpKernel
        {
            public Dictionary<string, int> Bump(Dictionary<string, int> scores, string key, HookContext ctx)
            {
                var result = new Dictionary<string, int>();
                if (scores.ContainsKey(key))
                {
                    result[key] = scores[key] + 1;
                }

                return result;
            }
        }
        """;

    private const string MapReplaceBodySource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("score-replace")]
        public sealed partial class ScoreReplaceKernel
        {
            public Dictionary<string, int> SetTwice(string key, int first, int second, HookContext ctx)
            {
                var result = new Dictionary<string, int>();
                result[key] = first;
                result[key] = second;
                return result;
            }
        }
        """;

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
            => "map-support";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastArguments = arguments;
            return ValueTask.FromResult(response);
        }
    }
}
