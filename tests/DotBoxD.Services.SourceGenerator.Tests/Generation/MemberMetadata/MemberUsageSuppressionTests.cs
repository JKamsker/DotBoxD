using FluentAssertions;
using Microsoft.CodeAnalysis;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation.MemberMetadata;

public sealed class MemberUsageSuppressionTests
{
    [Fact]
    public void ExperimentalAndObsoleteMembers_DoNotReportDiagnosticsInGeneratedCode()
    {
        var (final, runResult) = Run("""
            #pragma warning disable DBXBASE1
            using DotBoxD.Services.Attributes;
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;

            namespace Regress.MemberUsage
            {
                [RpcService]
                public interface IChild
                {
                    Task PingAsync();
                }

                [Experimental("DBXBASE1")]
                public interface IExperimentalBase
                {
                    Task InheritedAsync();
                }

                [RpcService]
                public interface IRoot : IExperimentalBase
                {
                    [Experimental("DBXMEM1")]
                    Task DoAsync();

                    [Experimental("DBXMEM2")]
                    IChild Child { get; }

                    [Obsolete("Use DoAsync")]
                    Task OldAsync();

                    [Obsolete]
                    Task OlderAsync();

                    [Obsolete("Custom", DiagnosticId = "DBXOBS1")]
                    Task CustomAsync();

                    [Obsolete("Use Child")]
                    IChild OldChild { get; }
                }
            }
            """);

        runResult.Diagnostics.Should().BeEmpty();
        var generatedTrees = runResult.GeneratedTrees.ToHashSet();
        final.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning &&
                d.Location.SourceTree is { } tree &&
                generatedTrees.Contains(tree))
            .Select(d => d.ToString())
            .Should()
            .BeEmpty();
        AssertCompiles(final);
    }

    [Theory]
    [InlineData("[Obsolete(\"Gone\", true)] Task OldAsync();", "OldAsync")]
    [InlineData("[Obsolete(\"Gone\", true)] IChild OldChild { get; }", "OldChild")]
    public void ErrorObsoleteMembers_AreRejectedWithServiceDiagnostic(string member, string memberName)
    {
        var (final, runResult) = Run($$"""
            using DotBoxD.Services.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace Regress.MemberUsage
            {
                [RpcService]
                public interface IChild
                {
                    Task PingAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    Task DoAsync();

                    {{member}}
                }
            }
            """);

        runResult.Diagnostics.Should().ContainSingle(d =>
            d.Id == "DBXS003" &&
            d.GetMessage().Contains("Regress.MemberUsage.IRoot", StringComparison.Ordinal) &&
            d.GetMessage().Contains($"'{memberName}'", StringComparison.Ordinal));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IRoot.", StringComparison.Ordinal));
        AssertCompiles(final);
    }
}
