namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class CallerCancellationHelperCollisionTests
{
    [Fact]
    public void Proxy_compiles_when_service_method_matches_cancellation_helper_signature()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Services.Attributes;

            namespace Collision;

            [RpcService]
            public interface IHelperCollision
            {
                Task __dotboxd_observeCallerCancellationAsync(Task task, CancellationToken ct);
            }
            """;

        var (final, _) = CodegenRegressionTestSupport.Run(source);

        CodegenRegressionTestSupport.AssertCompiles(final);
    }
}
