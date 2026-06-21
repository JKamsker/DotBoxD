using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

/// <summary>
/// In-process generator coverage for the <c>[HookResult]</c> builder surface: Ok()/Reject()/With&lt;Field&gt;()
/// emission, the IHookResult declaration, author-defined members being skipped, and the DBXK112 diagnostic when
/// the Success/Reason contract is missing.
/// </summary>
public sealed class HookResultGeneratorTests
{
    private const string ValidResult = """
        using DotBoxD.Abstractions;

        namespace Sample;

        [HookResult]
        public readonly partial record struct CombatDamageResult(bool Success, string? Reason, int Damage);
        """;

    [Fact]
    public void Generates_ok_reject_and_with_members_implementing_the_contract()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ValidResult));

        Assert.Contains(": global::DotBoxD.Abstractions.IHookResult", generated, StringComparison.Ordinal);
        Assert.Contains("public static CombatDamageResult Ok()", generated, StringComparison.Ordinal);
        Assert.Contains("public static CombatDamageResult Reject(string? reason = null)", generated, StringComparison.Ordinal);
        Assert.Contains("public CombatDamageResult WithDamage(int damage)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_emit_a_with_setter_for_control_fields()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ValidResult));

        Assert.DoesNotContain("WithSuccess(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("WithReason(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Skips_author_defined_members()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly partial record struct CombatDamageResult(bool Success, string? Reason, int Damage)
            {
                public static CombatDamageResult Ok() => new() { Success = true, Damage = 1 };
            }
            """;

        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources(source));

        // The author's Ok() must not be duplicated, but With<Field>() is still generated.
        Assert.DoesNotContain("public static CombatDamageResult Ok()", generated, StringComparison.Ordinal);
        Assert.Contains("public CombatDamageResult WithDamage(int damage)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_DBXK112_when_success_or_reason_is_missing()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly partial record struct BadResult(bool Success, int Damage);
            """;

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "DBXK112", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_partial_result_type_is_left_alone()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly record struct NotPartial(bool Success, string? Reason, int Damage);
            """;

        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources(source));

        Assert.DoesNotContain("NotPartial Ok()", generated, StringComparison.Ordinal);
    }
}
