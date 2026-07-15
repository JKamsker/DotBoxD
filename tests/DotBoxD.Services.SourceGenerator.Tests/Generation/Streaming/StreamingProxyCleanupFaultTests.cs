using System.Reflection;
using System.Runtime.Loader;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class StreamingProxyCleanupFaultTests
{
    [Fact]
    public async Task GeneratedProxy_PreservesReservationFailure_WhenReleaseThrows()
    {
        var (proxy, upload) = CreateThreeStreamProxy();
        var invoker = new ThrowingReleaseInvoker(FailureMode.ThirdReservation);
        var instance = Activator.CreateInstance(proxy, invoker)!;

        using var first = new MemoryStream(new byte[] { 1 });
        using var second = new MemoryStream(new byte[] { 2 });
        using var third = new MemoryStream(new byte[] { 3 });

        var ex = await CaptureUploadFailureAsync(upload, instance, first, second, third);

        ex.Should().BeOfType<InvalidOperationException>();
        ex.Message.Should().Be("third reservation failed");
        invoker.ReserveKinds.Should().Equal(
            RpcStreamKind.Binary,
            RpcStreamKind.Binary,
            RpcStreamKind.Binary);
        invoker.ReleasedStreamIds.Should().Equal(2, 1);
        invoker.InvokeCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GeneratedProxy_PreservesSynchronousInvokeFailure_WhenReleaseThrows()
    {
        var (proxy, upload) = CreateThreeStreamProxy();
        var invoker = new ThrowingReleaseInvoker(FailureMode.SynchronousInvoke);
        var instance = Activator.CreateInstance(proxy, invoker)!;

        using var first = new MemoryStream(new byte[] { 1 });
        using var second = new MemoryStream(new byte[] { 2 });
        using var third = new MemoryStream(new byte[] { 3 });

        var ex = await CaptureUploadFailureAsync(upload, instance, first, second, third);

        ex.Should().BeOfType<InvalidOperationException>();
        ex.Message.Should().Be("streamed invoke rejected");
        invoker.ReleasedStreamIds.Should().Equal(3, 2, 1);
        invoker.InvokeCalled.Should().BeTrue();
    }

    private static (Type Proxy, MethodInfo Upload) CreateThreeStreamProxy()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Behavior.StreamingCleanup
            {
                [RpcService]
                public interface IUpload
                {
                    Task<int> UploadAsync(
                        Stream first,
                        Stream second,
                        Stream third,
                        CancellationToken ct = default);
                }
            }
            """;

        var assembly = CompileWithGenerator(source);
        var proxyType = assembly.GetType("Behavior.StreamingCleanup.UploadProxy")!;
        var interfaceType = assembly.GetType("Behavior.StreamingCleanup.IUpload")!;
        return (proxyType, interfaceType.GetMethod("UploadAsync")!);
    }

    private static async Task<Exception> CaptureUploadFailureAsync(
        MethodInfo upload,
        object instance,
        Stream first,
        Stream second,
        Stream third)
    {
        try
        {
            var task = (Task)upload.Invoke(
                instance,
                new object[] { first, second, third, CancellationToken.None })!;
            return await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return ex.InnerException;
        }
    }

    private static Assembly CompileWithGenerator(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            throw new InvalidOperationException("Emit failed:\n" + errors);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("StreamingProxyCleanup_" + Guid.NewGuid(), isCollectible: false);
        return alc.LoadFromStream(ms);
    }

    private enum FailureMode
    {
        ThirdReservation,
        SynchronousInvoke,
    }

    private sealed class ThrowingReleaseInvoker(FailureMode failureMode) : IRpcInvoker
    {
        private int _reserveCount;

        public List<RpcStreamKind> ReserveKinds { get; } = new();

        public List<int> ReleasedStreamIds { get; } = new();

        public bool InvokeCalled { get; private set; }

        public RpcStreamHandle ReserveStream(RpcStreamKind kind)
        {
            ReserveKinds.Add(kind);
            var count = Interlocked.Increment(ref _reserveCount);
            if (failureMode == FailureMode.ThirdReservation && count == 3)
            {
                throw new InvalidOperationException("third reservation failed");
            }

            return new RpcStreamHandle(count, kind);
        }

        public void ReleaseStream(RpcStreamHandle handle)
        {
            ReleasedStreamIds.Add(handle.StreamId);
            throw new InvalidOperationException("release " + handle.StreamId + " failed");
        }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            RpcStreamAttachment[] streams,
            CancellationToken ct = default)
        {
            InvokeCalled = true;
            if (failureMode == FailureMode.SynchronousInvoke)
            {
                throw new InvalidOperationException("streamed invoke rejected");
            }

            throw new InvalidOperationException("invoke should not run");
        }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TResponse> InvokeAsync<TResponse>(
            string service,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeAsync<TRequest>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeAsync(
            string service,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeOnInstanceAsync<TRequest>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeOnInstanceAsync(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
