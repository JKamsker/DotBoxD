namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiImmutableArrayBuilderCapacitySetterReachabilityTests
{
    [Theory]
    [InlineData(
        "ImmutableArray<byte>.Builder Capacity setter",
        "private static readonly ImmutableArray<byte>.Builder Retained = CreateRetainedBuilder();",
        """
        private static ImmutableArray<byte>.Builder CreateRetainedBuilder()
        {
            var builder = ImmutableArray.CreateBuilder<byte>();
            builder.Capacity = int.MaxValue;
            return builder;
        }
        """,
        "System.Collections.Immutable.ImmutableArray")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin-immutable-array-setter.txt\");",
        "",
        "System.IO.File")]
    public async Task Reports_unbounded_immutable_array_builder_capacity_setter_static_initializer(
        string testCase,
        string staticMember,
        string helperMember,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(staticMember, helperMember),
            "DotBoxDPluginAnalyzerImmutableArrayBuilderCapacitySetterReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    private static string Source(string staticMember, string helperMember)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Immutable;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("immutable-array-builder-capacity-setter")]
                public sealed class ImmutableArrayBuilderCapacitySetterKernel : IEventKernel<string>
                {
                    {{staticMember}}

                    {{helperMember}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}
