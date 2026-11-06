using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

/// <summary>
/// Enums and DateOnly ride as Int on the wire, but C# interpolation formats them by name / date, not as the
/// raw integer. Only real <c>int</c> holes may use the invariant Int32 conversion; others must be rejected.
/// </summary>
public sealed class PluginAnalyzerStringInterpolationHoleTypeTests
{
    [Theory]
    [InlineData("$\"kind:{e.Kind}\"")]
    [InlineData("$\"day:{e.Day}\"")]
    [InlineData("$\"elapsed:{e.Elapsed}\"")]
    [InlineData("$\"ok:{e.Ok}\"")]
    public void Non_int_and_non_string_holes_report_DBXK100(string message)
        => Assert.Contains(
            PluginAnalyzerGeneratedPackageFactory.Diagnostics(Source(message)),
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("interpolation hole", StringComparison.Ordinal));

    [Theory]
    [InlineData("$\"amount:{e.Amount}\"")]
    [InlineData("$\"target:{e.TargetId}\"")]
    public void Int_and_string_holes_still_generate(string message)
        => Assert.DoesNotContain(
            PluginAnalyzerGeneratedPackageFactory.Diagnostics(Source(message)),
            diagnostic => diagnostic.Id == "DBXK100");

    private static string Source(string message)
        => $$"""
            using System;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public enum DamageKind { Ice = 1, Fire = 2 }

            public sealed record HoleEvent(string TargetId, int Amount, DamageKind Kind, DateOnly Day, TimeSpan Elapsed, bool Ok);

            [Plugin("interpolation-hole-types")]
            public sealed partial class HoleKernel : IEventKernel<HoleEvent>
            {
                public bool ShouldHandle(HoleEvent e, HookContext ctx) => true;

                public void Handle(HoleEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, {{message}});
            }
            """;
}
