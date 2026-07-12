using System.Reflection;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Testing;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class GeneratedExtensionFacadeGuardTests
{
    [Fact]
    public void GetExtension_NullPeer_ThrowsArgumentNullExceptionForPeer()
    {
        var extensions = LoadGeneratedExtensions();
        var method = GetExtensionMethod(extensions, "GetCalculator");

        var exception = InvokeAndUnwrap(method, new object?[] { null });

        var argumentException = Assert.IsType<ArgumentNullException>(exception);
        Assert.Equal("peer", argumentException.ParamName);
    }

    [Fact]
    public async Task ProvideExtension_NullImplementation_ThrowsArgumentNullExceptionForImplementation()
    {
        var extensions = LoadGeneratedExtensions();
        var method = GetExtensionMethod(extensions, "ProvideCalculator");
        var (first, second) = InMemoryRpcChannel.CreatePair();
        await using var peer = RpcPeer.Over(first, new TestJsonSerializer());
        await using (second.ConfigureAwait(false))
        {
            var exception = InvokeAndUnwrap(method, peer, null!);

            var argumentException = Assert.IsType<ArgumentNullException>(exception);
            Assert.Equal("implementation", argumentException.ParamName);
        }
    }

    private static Type LoadGeneratedExtensions()
    {
        var assembly = CompileAndLoad("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace GeneratedExtensionGuard.Sample
            {
                [RpcService]
                public interface ICalculator
                {
                    Task<int> AddAsync(int left, int right);
                }

                public sealed class Calculator : ICalculator
                {
                    public Task<int> AddAsync(int left, int right) => Task.FromResult(left + right);
                }
            }
            """);

        return assembly.GetType("DotBoxD.Services.Generated.DotBoxDGeneratedExtensions", throwOnError: true)!;
    }

    private static MethodInfo GetExtensionMethod(Type extensions, string methodName) =>
        extensions.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Generated extension method '{methodName}' was not emitted.");

    private static Exception InvokeAndUnwrap(MethodInfo method, params object?[] arguments)
    {
        try
        {
            _ = method.Invoke(null, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return ex.InnerException;
        }

        throw new InvalidOperationException("Expected the generated extension method to throw.");
    }
}
