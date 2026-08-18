using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncObsoleteAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_fire_async_extension_ignores_aliased_lookalike_obsolete_attribute()
    {
        var foreignObsolete = PluginServerGenerationTestDriver.CompileReference(
                """
                namespace System;

                public sealed class ObsoleteAttribute(string message, bool error) : Attribute;
                """,
                "ForeignObsolete")
            .WithAliases(ImmutableArray.Create("ForeignObsolete"));
        var result = PluginAnalyzerGeneratedPackageFactory.RunGeneratorWithReferences(
            """
            extern alias ForeignObsolete;

            using DotBoxD.Abstractions;

            namespace Regression.Game;

            [ForeignObsolete::System.Obsolete("foreign marker", true)]
            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageContext(int Amount);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Amount);
            """,
            foreignObsolete);

        Assert.DoesNotContain(result.Diagnostics, IsErrorObsoleteContextDiagnostic);
        Assert.Contains(result.GeneratedTrees, IsFireAsyncExtension);
    }

    [Fact]
    public void Generated_fire_async_extension_rejects_genuine_error_obsolete_attribute()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(
            """
            using System;
            using DotBoxD.Abstractions;

            namespace Regression.Game;

            [Obsolete("use another context", error: true)]
            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageContext(int Amount);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Amount);
            """);

        Assert.Contains(diagnostics, IsErrorObsoleteContextDiagnostic);
    }

    private static bool IsErrorObsoleteContextDiagnostic(Diagnostic diagnostic)
        => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
           diagnostic.Severity == DiagnosticSeverity.Error &&
           diagnostic.GetMessage().Contains("obsolete", StringComparison.OrdinalIgnoreCase) &&
           diagnostic.GetMessage().Contains("DamageContext", StringComparison.Ordinal);

    private static bool IsFireAsyncExtension(SyntaxTree tree)
        => tree.FilePath.EndsWith("DotBoxDHookFireAsyncExtensions.g.cs", StringComparison.Ordinal);
}
