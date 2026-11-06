using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Server-extension bodies lower <c>list.Add</c>/<c>map[key] = value</c> to value-returning kernel ops stored
/// back into the mutated local, so CLR reference aliases must be rejected, as must <c>using</c> declarations
/// whose Dispose call kernel IR cannot run.
/// </summary>
public sealed class ServerExtensionCollectionAliasRegressionTests
{
    [Fact]
    public void List_mutation_through_local_alias_reports_DBXK100()
        => AssertRejected(Body("""
            public int Count(int item, HookContext ctx)
            {
                var values = new List<int>();
                var alias = values;
                alias.Add(item);
                return values.Count;
            }
            """), "list/map mutation");

    [Fact]
    public void List_mutation_after_building_dto_from_it_reports_DBXK100()
        => AssertRejected(Body("""
            public int Count(int item, HookContext ctx)
            {
                var values = new List<int>();
                var holder = new Holder(values);
                values.Add(item);
                return holder.Values.Count;
            }
            """), "list/map mutation");

    [Fact]
    public void Map_parameter_mutation_through_alias_reports_DBXK100()
        => AssertRejected(Body("""
            public Dictionary<string, int> Count(Dictionary<string, int> scores, HookContext ctx)
            {
                var view = scores;
                scores["hero"] = 1;
                return view;
            }
            """), "list/map mutation");

    [Fact]
    public void Using_declaration_reports_DBXK100()
        => AssertRejected(Body("""
            public int Count(int item, HookContext ctx)
            {
                using var lease = new Lease(item);
                return lease.Value;
            }
            """), "'using' declarations");

    [Fact]
    public async Task Mutated_parameter_list_returned_by_value_still_runs()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Body("""
                public List<int> Append(List<int> values, int item, HookContext ctx)
                {
                    values.Add(item);
                    return values;
                }
                """),
            "Sample.AliasProbePluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync(
            [SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32), SandboxValue.FromInt32(2)]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal([1, 2], list.Values.Select(item => Assert.IsType<I32Value>(item).Value));
    }

    private static void AssertRejected(string source, string message)
        => Assert.Contains(
            PluginAnalyzerGeneratedPackageFactory.Diagnostics(source),
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(message, StringComparison.Ordinal));

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static string Body(string method)
        => $$"""
            using System;
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record Holder(List<int> Values);

            public sealed record Lease(int Value) : IDisposable
            {
                public void Dispose()
                {
                }
            }

            [ServerExtension("alias-probe")]
            public sealed partial class AliasProbeKernel
            {
                {{method}}
            }
            """;
}
