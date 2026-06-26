using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Guards finding #21: a scoped-handle local must evaluate its scope key once into the local and thread the
/// local into every subsequent call, instead of re-inlining the lowered key into each call. Here the key is an
/// effectful host binding (<c>ResolveKey()</c>); the handle is read twice (<c>GetThreat() + GetThreat()</c>).
/// Before the fix the lowered key was stored and re-inlined, so the effectful key was lowered into the handle
/// assignment AND into both calls (three evaluations); after the fix it appears once and the calls thread the
/// handle local.
/// </summary>
public sealed class ServerExtensionScopedHandleKeyReuseTests
{
    private const string Source = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        public interface IKeySource
        {
            [HostBinding("host.key.resolve", "game.key.resolve", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            string ResolveKey();
        }

        [DotBoxDService]
        public interface IGameWorldAccess
        {
            IMonsterControl Monsters { get; }
        }

        [DotBoxDService]
        public interface IMonsterControl
        {
            [HostCapability("game.world.monster.read.handle", HostBindingEffect.HostStateRead)]
            IMonster Get(string entityId);
        }

        [DotBoxDService]
        public interface IMonster
        {
            [HostCapability("game.world.monster.read.threat", HostBindingEffect.HostStateRead)]
            int GetThreat();
        }

        [ServerExtension(typeof(IMonsterControl))]
        public sealed partial class ScopedReuseKernel
        {
            private readonly IGameWorldAccess _world;
            public ScopedReuseKernel(IGameWorldAccess world) => _world = world;

            [ServerExtensionMethod]
            public int ReadTwice(HookContext ctx)
            {
                var monster = _world.Monsters.Get(ctx.Host<IKeySource>().ResolveKey());
                return monster.GetThreat() + monster.GetThreat();
            }
        }
        """;

    [Fact]
    public void Scoped_handle_local_evaluates_an_effectful_key_once_and_threads_the_local()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ScopedReusePluginPackage",
            typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute));

        var calls = package.Module.Functions
            .SelectMany(function => function.Body)
            .SelectMany(Calls)
            .ToArray();

        // The effectful scope key is lowered exactly once (the handle-local assignment), never re-inlined.
        Assert.Single(calls, call => call.Name == "host.key.resolve");

        // Every GetThreat call threads the handle local (which holds the captured key), not the inlined key call.
        var threatCalls = calls
            .Where(call => call.Name.EndsWith(".GetThreat", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, threatCalls.Length);
        foreach (var call in threatCalls)
        {
            Assert.IsType<VariableExpression>(Assert.Single(call.Arguments));
        }
    }

    private static IEnumerable<CallExpression> Calls(Statement statement)
        => Expressions(statement).OfType<CallExpression>();

    private static IEnumerable<Expression> Expressions(Statement statement)
        => statement switch
        {
            AssignmentStatement assignment => Expressions(assignment.Value),
            ReturnStatement @return => Expressions(@return.Value),
            ExpressionStatement expression => Expressions(expression.Value),
            IfStatement branch => Expressions(branch.Condition)
                .Concat(branch.Then.SelectMany(Expressions))
                .Concat(branch.Else.SelectMany(Expressions)),
            WhileStatement loop => Expressions(loop.Condition).Concat(loop.Body.SelectMany(Expressions)),
            ForRangeStatement loop => Expressions(loop.Start)
                .Concat(Expressions(loop.End))
                .Concat(loop.Body.SelectMany(Expressions)),
            _ => []
        };

    private static IEnumerable<Expression> Expressions(Expression expression)
    {
        yield return expression;
        switch (expression)
        {
            case UnaryExpression unary:
                foreach (var inner in Expressions(unary.Operand))
                {
                    yield return inner;
                }

                break;
            case BinaryExpression binary:
                foreach (var inner in Expressions(binary.Left).Concat(Expressions(binary.Right)))
                {
                    yield return inner;
                }

                break;
            case CallExpression call:
                foreach (var inner in call.Arguments.SelectMany(Expressions))
                {
                    yield return inner;
                }

                break;
        }
    }
}
