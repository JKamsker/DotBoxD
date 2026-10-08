using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiReferenceSemanticsTests
{
    [Theory]
    [InlineData("var list = new List<int>(); var alias = list; list.Add(value); return alias.Count;")]
    [InlineData("var list = new List<int>(); var alias = list; alias.Add(value); return list.Count;")]
    [InlineData("var list = new List<int>(); IReadOnlyList<int> alias = (IReadOnlyList<int>)list; list.Add(value); return alias.Count;")]
    [InlineData("var list = new List<int>(); List<int> alias; alias = list; list.Add(value); return alias.Count;")]
    [InlineData("var list = new List<int>(); var holder = new Holder(list); list.Add(value); return holder.Values.Count;")]
    [InlineData("var map = new Dictionary<int, int>(); var alias = map; map[value] = value; return alias.ContainsKey(value) ? 1 : 0;")]
    [InlineData("var map = new Dictionary<int, int>(); var alias = map; alias[value] = value; return map.ContainsKey(value) ? 1 : 0;")]
    [InlineData("var list = new List<int>(); list.Add(value); foreach (var item in list) { list.Add(item); } return list.Count;")]
    public void Mutable_collection_aliases_fail_closed_at_compile_time(string body) => AssertUnsupported(body);

    [Theory]
    [InlineData("using var resource = new Resource(value); return resource.Value;")]
    [InlineData("{ using var resource = new Resource(value); return resource.Value; }")]
    public void Using_declarations_fail_closed_in_every_handler_scope(string body) => AssertUnsupported(body);

    [Fact]
    public void Distinct_scoped_locals_with_same_name_fail_closed_before_installation()
        => AssertUnsupported("{ var item = value; value = item + 1; } { var item = \"done\"; _ = item; return value; }");

    [Theory]
    [InlineData("var list = new List<int>(); list.Add(value); return list.Count;", 1)]
    [InlineData("var map = new Dictionary<int, int>(); map[value] = value; return map[value];", 42)]
    [InlineData("var list = new List<int>(); var alias = list; return alias.Count;", 0)]
    [InlineData("var resource = new Resource(value); return resource.Value;", 42)]
    public async Task Unaliased_mutation_and_ordinary_value_locals_preserve_execution(string body, int expected)
    {
        var package = UiGeneratorFixture.Package(Source(body));
        using var sandbox = UiTestFixture.Sandbox();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(package, new RecordingUiRenderer());
        Assert.Equal(expected, (await session.DispatchAsync(1)).State[0].Value.Integer);
    }

    private static void AssertUnsupported(string body)
    {
        var generated = UiGeneratorFixture.Generate(HandlerSource(body));
        Assert.Empty(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var diagnostic = Assert.Single(generated.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Contains("typed RPC", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(generated.Result.GeneratedTrees);
    }

    private static string HandlerSource(string body) => """
        using System.Collections.Generic;
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        public sealed record Resource(int Value) : System.IDisposable
        {
            public void Dispose() => throw new System.InvalidOperationException("dispose");
        }
        public sealed record Holder(List<int> Values);
        public static partial class Counter
        {
            [UiLocalHandler] public static int Handle(int value) {
        """ + body + """
            }
        }
        """;

    internal static string Source(string body) => HandlerSource(body) + """
        public static partial class Counter
        {
            public static UiPackage Package()
            {
                var b = new UiBuilder();
                var state = b.State(42);
                return b.Build(b.Button("Run", b.Kernel(HandleUiKernel(), state), state));
            }
        }
        """;
}
