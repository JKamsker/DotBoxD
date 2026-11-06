using FluentAssertions;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public sealed class SubServicePropertyCycleTests
{
    [Fact]
    public void CyclicSubServiceProperties_AreRejectedInsteadOfRecursingForever()
    {
        var (final, runResult) = Run("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServiceCycles
            {
                [RpcService]
                public interface IFolder
                {
                    IFolder Parent { get; }
                }

                [RpcService]
                public interface IGame
                {
                    IPlayers Players { get; }

                    Task PingAsync();
                }

                [RpcService]
                public interface IPlayers
                {
                    IGame Game { get; }
                }

                [RpcService]
                public interface IHost
                {
                    IGame Game { get; }
                }
            }
            """);

        AssertCompiles(final);
        AssertCycleDiagnostic(runResult, "IFolder", "Parent");
        AssertCycleDiagnostic(runResult, "IGame", "Players");
        AssertCycleDiagnostic(runResult, "IPlayers", "Game");

        var generated = runResult.Results.Single().GeneratedSources;
        generated.Should().NotContain(g =>
            g.HintName.Contains("IFolder.", StringComparison.Ordinal) ||
            g.HintName.Contains("IGame.", StringComparison.Ordinal) ||
            g.HintName.Contains("IPlayers.", StringComparison.Ordinal) ||
            g.HintName.Contains("IHost.", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedSubServiceReachedTwice_IsNotACycle()
    {
        var (final, runResult) = Run("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServiceDiamond
            {
                [RpcService]
                public interface IShared
                {
                    Task PingAsync();
                }

                [RpcService]
                public interface ILeft
                {
                    IShared Shared { get; }
                }

                [RpcService]
                public interface IRight
                {
                    IShared Shared { get; }
                }

                [RpcService]
                public interface IRoot
                {
                    ILeft Left { get; }

                    IRight Right { get; }
                }
            }
            """);

        AssertCompiles(final);
        runResult.Diagnostics.Should().BeEmpty();
        runResult.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName.Contains("IRoot.DotBoxDRpcProxy", StringComparison.Ordinal));
    }

    private static void AssertCycleDiagnostic(
        Microsoft.CodeAnalysis.GeneratorDriverRunResult runResult,
        string interfaceName,
        string propertyName)
        => runResult.Diagnostics.Should().ContainSingle(d =>
            d.Id == "DBXS003" &&
            d.GetMessage().Contains("Regress.SubServiceCycles." + interfaceName + "'", StringComparison.Ordinal) &&
            d.GetMessage().Contains($"sub-service property '{propertyName}' leads back to '{interfaceName}'", StringComparison.Ordinal));
}
