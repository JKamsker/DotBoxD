using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

/// <summary>
/// A [KernelMethod] argument is only "repeatable" (safe to drop or evaluate twice when inlined) if evaluating
/// it cannot throw. Integer <c>/</c> and <c>%</c> throw on a zero divisor and checked arithmetic throws on
/// overflow, so dropping an unused <c>100 / e.Count</c> would silently lose C#'s exception.
/// </summary>
public sealed class PluginAnalyzerKernelMethodTrappingArgumentTests
{
    [Theory]
    [InlineData("100 / e.Count")]
    [InlineData("100 % e.Count")]
    [InlineData("e.Count / -1")]
    [InlineData("e.Count % -1")]
    [InlineData("e.Count / (2 - 2 + e.Count)")]
    [InlineData("checked(e.Count + 1) + 0")]
    [InlineData("checked(-e.Count) + 0")]
    public void Unused_parameter_with_trapping_argument_reports_DBXK100(string argument)
        => Assert.Contains(
            PluginAnalyzerGeneratedPackageFactory.Diagnostics(Source(argument)),
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("parameter 'ratio' is not used", StringComparison.Ordinal));

    [Theory]
    [InlineData("e.Count + 1")]
    [InlineData("-e.Count")]
    [InlineData("e.Count * 2")]
    [InlineData("e.Count / 2")]
    [InlineData("e.Count % 3")]
    [InlineData("e.Count / -2")]
    public void Unused_parameter_with_non_trapping_argument_still_generates(string argument)
        => Assert.DoesNotContain(
            PluginAnalyzerGeneratedPackageFactory.Diagnostics(Source(argument)),
            diagnostic => diagnostic.Id == "DBXK100");

    // Integer division by a constant other than 0 and -1 cannot throw, so a reused parameter may be
    // evaluated twice; a variable divisor still can, so it stays rejected.
    [Theory]
    [InlineData("e.Count / 2", false)]
    [InlineData("e.Count % 2", false)]
    [InlineData("e.Count / -1", true)]
    [InlineData("100 / e.Count", true)]
    public void Reused_parameter_with_division_argument_reports_DBXK100_only_when_it_can_trap(
        string argument,
        bool expectDiagnostic)
    {
        var reported = PluginAnalyzerGeneratedPackageFactory.Diagnostics(ReusedSource(argument))
            .Any(diagnostic => diagnostic.Id == "DBXK100" &&
                               diagnostic.GetMessage().Contains("parameter 'x' is used more than once", StringComparison.Ordinal));

        Assert.Equal(expectDiagnostic, reported);
    }

    private static string ReusedSource(string argument)
        => $$"""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record GateEvent(string TargetId, bool On, int Count);

            [Plugin("trapping-kernel-method-reused-argument")]
            public sealed partial class GateKernel : IEventKernel<GateEvent>
            {
                public bool ShouldHandle(GateEvent e, HookContext ctx) => Twice({{argument}}) > 3;

                public void Handle(GateEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "hit");

                [KernelMethod]
                public static int Twice(int x) => x + x;
            }
            """;

    private static string Source(string argument)
        => $$"""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record GateEvent(string TargetId, bool On, int Count);

            [Plugin("trapping-kernel-method-argument")]
            public sealed partial class GateKernel : IEventKernel<GateEvent>
            {
                public bool ShouldHandle(GateEvent e, HookContext ctx) => Gate(e.On, {{argument}});

                public void Handle(GateEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "hit");

                [KernelMethod]
                public static bool Gate(bool on, int ratio) => on;
            }
            """;
}
