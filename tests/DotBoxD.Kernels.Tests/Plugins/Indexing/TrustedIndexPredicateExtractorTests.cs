using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Indexing;

namespace DotBoxD.Kernels.Tests.Plugins.Indexing;

public sealed class TrustedIndexPredicateExtractorTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public void Extract_stops_after_assignment_to_event_backed_parameter()
    {
        var package = Package(
        [
            new AssignmentStatement("e_Damage", Int(0), Span),
            new ReturnStatement(DamageAtLeastFive(), Span)
        ]);

        var predicates = TrustedIndexPredicateExtractor.Extract(package, EventParameters());

        Assert.Empty(predicates);
    }

    [Fact]
    public void Extract_continues_after_assignment_to_non_event_local()
    {
        var package = Package(
        [
            new AssignmentStatement("scratch", Int(0), Span),
            new ReturnStatement(DamageAtLeastFive(), Span)
        ]);

        var predicate = Assert.Single(TrustedIndexPredicateExtractor.Extract(package, EventParameters()));

        Assert.Equal("Damage", predicate.Path);
        Assert.Equal(IndexPredicateOperator.GreaterThanOrEqual, predicate.Operator);
        Assert.Equal(5, predicate.Value);
        Assert.Equal("int", predicate.ValueType);
    }

    private static IReadOnlyList<Parameter> EventParameters()
        => [new("e_Damage", SandboxType.I32)];

    private static Expression DamageAtLeastFive()
        => new BinaryExpression(
            new VariableExpression("e_Damage", Span),
            ">=",
            Int(5),
            Span);

    private static LiteralExpression Int(int value)
        => new(SandboxValue.FromInt32(value), Span);

    private static PluginPackage Package(IReadOnlyList<Statement> shouldHandleBody)
    {
        var shouldHandle = new SandboxFunction(
            "should",
            IsEntrypoint: true,
            EventParameters(),
            SandboxType.Bool,
            shouldHandleBody);
        var handle = new SandboxFunction(
            "handle",
            IsEntrypoint: true,
            EventParameters(),
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);

        var module = new SandboxModule(
            "test.index",
            SemVersion.One,
            SemVersion.One,
            [],
            [shouldHandle, handle],
            new Dictionary<string, string>());
        var manifest = new PluginManifest(
            "test.index",
            "DotBoxD.Abstractions.IEventKernel<TestEvent>",
            ExecutionMode.Interpreted,
            [],
            [],
            [new HookSubscriptionManifest("TestEvent", "IndexKernel")]);

        return PluginPackage.Create(manifest, module, new KernelEntrypoints("should", "handle"));
    }
}
