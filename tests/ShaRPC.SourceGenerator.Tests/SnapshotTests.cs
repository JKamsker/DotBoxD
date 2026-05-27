using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using VerifyXunit;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Snapshot tests for ShaRpcGenerator. Snapshots live in the Snapshots/ subfolder
/// next to this file and are accepted via Verify's standard flow.
/// </summary>
public class SnapshotTests
{
    private const string SingleMethodService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.One
        {
            [ShaRpcService]
            public interface ICalculator
            {
                Task<int> AddAsync(int a, int b);
            }
        }
        """;

    private const string MixedReturnsService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Mixed
        {
            [ShaRpcService]
            public interface IMix
            {
                Task<string> GetNameAsync();
                Task SaveAsync(string value);
                int SyncAdd(int a, int b);
                void SyncPing();
            }
        }
        """;

    private const string CustomNameService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Renamed
        {
            [ShaRpcService(Name = "Greeter")]
            public interface IHello
            {
                [ShaRpcMethod(Name = "Greet")]
                Task<string> HelloAsync(string who);
            }
        }
        """;

    private const string TwoServices = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Two
        {
            [ShaRpcService]
            public interface IOne
            {
                Task<int> AAsync(int x);
            }

            [ShaRpcService]
            public interface ITwo
            {
                Task<string> BAsync();
            }
        }
        """;

    [Fact]
    public Task SingleMethod() => RunVerify(SingleMethodService);

    [Fact]
    public Task MixedReturns() => RunVerify(MixedReturnsService);

    [Fact]
    public Task CustomNames() => RunVerify(CustomNameService);

    [Fact]
    public Task TwoServicesInOneCompilation() => RunVerify(TwoServices);

    private static Task RunVerify(string source)
    {
        var (driver, _) = GeneratorTestHelper.RunGenerator(source);
        return Verifier.Verify(driver).UseDirectory("Snapshots");
    }
}
