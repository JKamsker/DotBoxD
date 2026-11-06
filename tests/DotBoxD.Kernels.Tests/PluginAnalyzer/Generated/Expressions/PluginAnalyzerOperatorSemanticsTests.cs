using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

/// <summary>
/// The handle (ShouldHandle/hook-chain) lowerer carries TimeSpan/TimeOnly as I64 ticks, DateOnly as an I32 day
/// number, float as F64 and enums as their I32/I64 wire integer. Operators whose C# semantics differ on those
/// wire values must be rejected exactly like the server-extension lowerer rejects them.
/// </summary>
public sealed class PluginAnalyzerOperatorSemanticsTests
{
    [Theory]
    [InlineData("e.Elapsed / e.Budget > 1")]
    [InlineData("e.Closes - e.Opens > e.Budget")]
    [InlineData("e.Elapsed + e.Budget > e.Budget")]
    [InlineData("-e.Elapsed < e.Budget")]
    public void User_defined_framework_operators_report_DBXK100(string predicate)
        => AssertRejected(predicate, "user-defined");

    [Theory]
    [InlineData("e.Elapsed > e.Budget")]
    [InlineData("e.Closes >= e.Opens")]
    [InlineData("e.Day == e.OtherDay")]
    [InlineData("e.Speed > e.Ratio")]
    [InlineData("e.Ratio * 2 > 1")]
    [InlineData("e.Kind == Kind.Fire")]
    [InlineData("e.Flags == Big.High")]
    public void Wire_preserving_operators_still_generate(string predicate)
        => Assert.DoesNotContain(
            PluginAnalyzerGeneratedPackageFactory.Diagnostics(Source(predicate)),
            diagnostic => diagnostic.Id == "DBXK100");

    [Theory]
    [InlineData("e.Speed * e.Speed > 1f")]
    [InlineData("e.Speed + 1f > 1f")]
    [InlineData("e.Speed > e.Amount")]
    public void Single_precision_arithmetic_reports_DBXK100(string predicate)
        => AssertRejected(predicate, "System.Single");

    [Theory]
    [InlineData("e.Flags > Big.Low")]
    [InlineData("e.Flags is > Big.Low")]
    [InlineData("e.Flags is not (>= Big.High)")]
    public void Unsigned_64_bit_enum_ordering_reports_DBXK100(string predicate)
        => AssertRejected(predicate, "ulong-backed enums");

    [Theory]
    [InlineData("e.Kind + 1 == Kind.Ice")]
    [InlineData("e.Kind - Kind.Fire == 1")]
    public void Narrow_enum_arithmetic_reports_DBXK100(string predicate)
        => AssertRejected(predicate, "underlying-width");

    private static void AssertRejected(string predicate, string message)
        => Assert.Contains(
            PluginAnalyzerGeneratedPackageFactory.Diagnostics(Source(predicate)),
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(message, StringComparison.Ordinal));

    private static string Source(string predicate)
        => $$"""
            using System;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public enum Kind : byte { Fire = 1, Ice = 2 }

            public enum Big : ulong { Low = 1, High = 0x8000_0000_0000_0000 }

            public sealed record ProbeEvent(
                string TargetId,
                TimeSpan Elapsed,
                TimeSpan Budget,
                TimeOnly Opens,
                TimeOnly Closes,
                DateOnly Day,
                DateOnly OtherDay,
                float Speed,
                double Ratio,
                int Amount,
                Kind Kind,
                Big Flags);

            [Plugin("operator-semantics")]
            public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
            {
                public bool ShouldHandle(ProbeEvent e, HookContext ctx) => {{predicate}};

                public void Handle(ProbeEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "hit");
            }
            """;
}
