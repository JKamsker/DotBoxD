using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class RunLocalIpcBoundaryTests
{
    [Theory]
    [InlineData(RemoteBoundaryKind.Subscription)]
    [InlineData(RemoteBoundaryKind.Hook)]
    public async Task Known_remote_RunLocal_chains_place_the_ipc_boundary_after_Where_and_Select(
        RemoteBoundaryKind kind)
    {
        var compiled = Compile(KnownRemoteRegistrySource(kind), "DotBoxDKnownRunLocalBoundaryTest");

        AssertGeneratedBoundary(compiled.GeneratedSources, kind);
        var callback = ConfigureKnownRemoteRegistry(compiled.Assembly, kind);

        Assert.Equal(RemoteLocalCallbackPayloadKind.Projection, callback.Payload.Kind);
        Assert.Equal(typeof(string), callback.Payload.Type);
        Assert.Equal(callback.Package.Entrypoints.Handle, callback.Payload.Entrypoint);
        Assert.DoesNotContain(PluginMessageBindings.CapabilityId, callback.Package.Manifest.RequiredCapabilities);

        using var server = DotBoxD.Plugins.PluginServer.Create();
        var kernel = await server.InstallLocalCallbackAsync(callback.Package);
        var adapter = server.Events.Resolve<ChainAggroEvent>();

        var acceptedPayload = await kernel.TryEvaluateHandleAsync(adapter, new ChainAggroEvent("monster-1", 3));
        Assert.Equal("monster-1", Assert.IsType<StringValue>(acceptedPayload).Value);

        var rejectedPayload = await kernel.TryEvaluateHandleAsync(adapter, new ChainAggroEvent("monster-2", 10));
        Assert.Null(rejectedPayload);

        var messages = new RecordingMessageSink();
        var handler = Assert.IsType<Func<string, HookContext, ValueTask>>(callback.Handler);
        await handler("monster-1", new HookContext(messages, CancellationToken.None));

        Assert.Equal(["monster-1"], Seen(compiled.Assembly));
        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("plugin-side", message.Message);
    }

    [Theory]
    [InlineData(RemoteBoundaryKind.Subscription)]
    [InlineData(RemoteBoundaryKind.Hook)]
    public void Generated_server_RunLocal_fallback_places_the_ipc_boundary_after_Where_and_Select(
        RemoteBoundaryKind kind)
    {
        var generatedSources = GenerateOnly(GeneratedServerSource(kind), "DotBoxDFallbackRunLocalBoundaryTest");

        AssertGeneratedBoundary(generatedSources, kind);
    }

    private static void AssertGeneratedBoundary(IReadOnlyList<string> generatedSources, RemoteBoundaryKind kind)
    {
        var interceptor = Assert.Single(
            generatedSources,
            source => source.Contains("HookChainInterceptors", StringComparison.Ordinal));

        Assert.Contains("UseGeneratedLocalCallbackChain", interceptor, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(", interceptor, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select(", interceptor, StringComparison.Ordinal);
        Assert.Contains(kind.StageTypeName(), interceptor, StringComparison.Ordinal);
        Assert.Contains(nameof(ChainAggroEvent), interceptor, StringComparison.Ordinal);
        Assert.True(
            interceptor.Contains("global::System.String> pipeline", StringComparison.Ordinal) ||
            interceptor.Contains("string> pipeline", StringComparison.Ordinal),
            interceptor);
        Assert.True(
            interceptor.Contains("global::System.Func<global::System.String", StringComparison.Ordinal) ||
            interceptor.Contains("global::System.Func<string", StringComparison.Ordinal),
            interceptor);
    }

    private static RemoteLocalCallbackRegistration ConfigureKnownRemoteRegistry(
        Assembly assembly,
        RemoteBoundaryKind kind)
    {
        var callbacks = new List<RemoteLocalCallbackRegistration>();
        object registry = kind switch
        {
            RemoteBoundaryKind.Subscription => new RemoteSubscriptionRegistry(
                package => ValueTask.FromResult(package.Manifest.PluginId),
                registration =>
                {
                    callbacks.Add(registration);
                    return ValueTask.FromResult(registration.Package.Manifest.PluginId);
                }),
            RemoteBoundaryKind.Hook => new RemoteHookRegistry(
                package => ValueTask.FromResult(package.Manifest.PluginId),
                registration =>
                {
                    callbacks.Add(registration);
                    return ValueTask.FromResult(registration.Package.Manifest.PluginId);
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        assembly.GetType("BoundarySample.RemoteUsage")!
            .GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry]);

        var callback = Assert.Single(callbacks);
        Assert.Equal(typeof(ChainAggroEvent), callback.EventType);
        return callback;
    }

    private static BoundaryCompilation Compile(string source, string assemblyName)
    {
        var parseOptions = InterceptorParseOptions();
        var compilation = CreateCompilation(source, assemblyName, parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));

        return new BoundaryCompilation(
            Assembly.Load(stream.ToArray()),
            driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetText().ToString()).ToArray());
    }

    private static IReadOnlyList<string> GenerateOnly(string source, string assemblyName)
    {
        var parseOptions = InterceptorParseOptions();
        var compilation = CreateCompilation(source, assemblyName, parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGenerators(compilation);

        var result = driver.GetRunResult();
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return result.GeneratedTrees.Select(tree => tree.GetText().ToString()).ToArray();
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName,
        CSharpParseOptions parseOptions)
        => CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(ChainAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static CSharpParseOptions InterceptorParseOptions()
        => CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    private static IReadOnlyList<string> Seen(Assembly assembly)
        => (IReadOnlyList<string>)assembly.GetType("BoundarySample.RemoteUsage")!
            .GetField("Seen", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    private static string KnownRemoteRegistrySource(RemoteBoundaryKind kind)
        => $$"""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using DotBoxD.Plugins.Runtime;

            namespace BoundarySample;

            public static class RemoteUsage
            {
                public static readonly List<string> Seen = new();

                public static void Configure({{kind.RegistryTypeName()}} registry)
                    => registry.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Select(e => e.MonsterId)
                        .RunLocal((id, ctx) =>
                        {
                            ctx.Messages.Send(id, "plugin-side");
                            Seen.Add(id);
                            return ValueTask.CompletedTask;
                        });
            }
            """;

    private static string GeneratedServerSource(RemoteBoundaryKind kind)
        => $$"""
            using System.Threading.Tasks;
            using DotBoxD.Plugins.Runtime;

            namespace BoundarySample;

            public static class RemoteUsage
            {
                public static void Configure(IGeneratedWorldServer server)
                    => server.{{kind.GeneratedServerPropertyName()}}
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Select(e => e.MonsterId)
                        .RunLocal((id, ctx) => ValueTask.CompletedTask);
            }
            """;

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private sealed record BoundaryCompilation(Assembly Assembly, IReadOnlyList<string> GeneratedSources);

    private sealed class RecordingMessageSink : IPluginMessageSink
    {
        private readonly ConcurrentQueue<PluginMessage> _messages = [];

        public IReadOnlyCollection<PluginMessage> Messages => _messages.ToArray();

        public void Send(string targetId, string message)
            => _messages.Enqueue(new PluginMessage(targetId, message));

        public ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Send(targetId, message);
            return ValueTask.CompletedTask;
        }
    }
}

public enum RemoteBoundaryKind
{
    Subscription,
    Hook
}

internal static class RemoteBoundaryKindExtensions
{
    public static string RegistryTypeName(this RemoteBoundaryKind kind)
        => kind switch
        {
            RemoteBoundaryKind.Subscription => "RemoteSubscriptionRegistry",
            RemoteBoundaryKind.Hook => "RemoteHookRegistry",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static string GeneratedServerPropertyName(this RemoteBoundaryKind kind)
        => kind switch
        {
            RemoteBoundaryKind.Subscription => "Subscriptions",
            RemoteBoundaryKind.Hook => "Hooks",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static string StageTypeName(this RemoteBoundaryKind kind)
        => kind switch
        {
            RemoteBoundaryKind.Subscription => "RemoteSubscriptionStage",
            RemoteBoundaryKind.Hook => "RemoteHookStage",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}
