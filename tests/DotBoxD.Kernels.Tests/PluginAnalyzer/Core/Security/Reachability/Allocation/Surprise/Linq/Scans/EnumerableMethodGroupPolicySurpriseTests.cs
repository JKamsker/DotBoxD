namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class EnumerableMethodGroupPolicySurpriseTests
{
    [Theory]
    [InlineData("Func<IEnumerable<int>, int>", "Sum")]
    [InlineData("Func<IEnumerable<int>, IEnumerable<int>>", "Reverse")]
    [InlineData("Func<IEnumerable<int>, int>", "Last")]
    [InlineData("Func<IEnumerable<int>, int>", "Count")]
    [InlineData("Func<IEnumerable<int>, int>", "First")]
    [InlineData("Func<IEnumerable<int>, int[]>", "ToArray")]
    public async Task Local_and_field_delegates_cannot_hide_unbounded_operations(string signature, string name)
    {
        foreach (var field in new[] { false, true })
        {
            var declaration = signature + " scan = Enumerable." + name + ";";
            var source = $$"""
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                [Plugin("delegate-scan")]
                public sealed class Kernel : IEventKernel<string>
                {
                    {{(field ? "private static readonly " + declaration : "")}}
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{(field ? "" : declaration)}}
                        _ = scan(Array.Empty<int>());
                        return true;
                    }
                    public void Handle(string e, HookContext context) { }
                }
                """;
            var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "MethodGroup" + name + field);
            Assert.Contains(diagnostics, d => d.Id == "DBXK001" &&
                d.GetMessage().Contains("System.Linq.Enumerable." + name, StringComparison.Ordinal));
        }
    }
}
