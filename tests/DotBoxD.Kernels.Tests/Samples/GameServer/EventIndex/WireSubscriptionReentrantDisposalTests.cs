extern alias GameServerAbstractions;

using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Indexing;
using DotBoxD.Plugins.Policies;
using GameServerAbstractions::DotBoxD.Kernels.Game.Server.Abstractions.Events;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class WireSubscriptionReentrantDisposalTests
{
    [Fact]
    public async Task Reentrant_disposal_during_classification_does_not_poison_shared_index_registry()
    {
        var registry = new EventIndexRegistry();
        var package = GeneratedAttackPackage();

        using var serverA = PluginServer.Create(new RecordingMessageSink(), defaultPolicy: ChainPolicy());
        _ = serverA.Events.Resolve<AttackEvent>();
        var kernelA = await serverA.InstallAsync(package, ChainPolicy());

        var firstException = Record.Exception(() => serverA.WireSubscription(
            kernelA,
            new WireOptions
            {
                IndexRegistry = registry,
                ClassifyOverride = terminal =>
                {
                    serverA.Dispose();
                    return terminal;
                }
            }));

        var sinkB = new RecordingMessageSink();
        using var serverB = PluginServer.Create(sinkB, defaultPolicy: ChainPolicy());
        _ = serverB.Events.Resolve<AttackEvent>();
        var kernelB = await serverB.InstallAsync(package, ChainPolicy());

        var secondException = Record.Exception(() => serverB.WireSubscription(
            kernelB,
            new WireOptions { IndexRegistry = registry }));

        Assert.Null(secondException);
        Assert.IsType<ObjectDisposedException>(firstException);

        registry.Publish(new AttackEvent("player-1", "player-2", Damage: 7, AttackerLevel: 8));
        await registry.DrainAsync();

        var message = Assert.Single(sinkB.Messages);
        Assert.Equal("player-2", message.TargetId);
        Assert.Equal("indexed-taunt:inline", message.Message);
    }

    private static PluginPackage GeneratedAttackPackage()
    {
        const string chain = """
            subscriptions.On<global::DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent>()
                .Where(e => e.AttackerId == "player-1" && e.Damage >= 5)
                .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "indexed-taunt:inline"));
            """;

        var source = $$"""
            using DotBoxD.Plugins.Runtime;

            namespace ChainSample;

            public static class Usage
            {
                public static void Configure(SubscriptionRegistry subscriptions)
                    => {{chain}}
            }
            """;

        var assembly = Compile(source);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes) is not null);
        return (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!
            .Invoke(null, null)!;
    }

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDEventIndexReentrantDisposalTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(AttackEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static SandboxPolicy ChainPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));

    private sealed class RecordingMessageSink : IPluginMessageSink
    {
        private readonly ConcurrentQueue<PluginMessage> _messages = [];

        public IReadOnlyCollection<PluginMessage> Messages => _messages.ToArray();

        public void Send(string targetId, string message) => _messages.Enqueue(new PluginMessage(targetId, message));

        public ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Send(targetId, message);
            return ValueTask.CompletedTask;
        }
    }
}
