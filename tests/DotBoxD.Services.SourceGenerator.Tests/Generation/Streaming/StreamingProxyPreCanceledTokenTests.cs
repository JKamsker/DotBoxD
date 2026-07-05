using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation.Streaming;

public sealed class StreamingProxyPreCanceledTokenTests
{
    [Fact]
    public async Task GeneratedProxy_DoesNotReserveRequestStreams_WhenMethodTokenIsAlreadyCanceled()
    {
        var (proxy, upload, invoker) = CreateUploadProxy();
        using var bytes = new MemoryStream(new byte[] { 1, 2, 3 });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InvokeTaskAsync(upload, proxy, bytes, cts.Token));

        ex.CancellationToken.Should().Be(cts.Token);
        invoker.ReserveCount.Should().Be(0);
        invoker.ReleaseCount.Should().Be(0);
        invoker.StreamedInvokeCount.Should().Be(0);
    }

    [Fact]
    public async Task GeneratedProxy_ReportsCancellationInsteadOfNullStream_WhenMethodTokenIsAlreadyCanceled()
    {
        var (proxy, upload, invoker) = CreateUploadProxy();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InvokeTaskAsync(upload, proxy, null!, cts.Token));

        ex.CancellationToken.Should().Be(cts.Token);
        invoker.ReserveCount.Should().Be(0);
        invoker.ReleaseCount.Should().Be(0);
        invoker.StreamedInvokeCount.Should().Be(0);
    }

    private static (object Proxy, MethodInfo Upload, RecordingInvoker Invoker) CreateUploadProxy()
    {
        var assembly = CompileWithGenerator(UploadServiceSource);
        var proxyType = assembly.GetType("Behavior.StreamingPreCancel.UploadProxy")!;
        var interfaceType = assembly.GetType("Behavior.StreamingPreCancel.IUpload")!;
        var invoker = new RecordingInvoker();
        var proxy = Activator.CreateInstance(proxyType, invoker)!;
        var upload = interfaceType.GetMethod("UploadAsync")!;
        return (proxy, upload, invoker);
    }

    private const string UploadServiceSource = """
        using DotBoxD.Services.Attributes;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Behavior.StreamingPreCancel
        {
            [RpcService]
            public interface IUpload
            {
                Task<int> UploadAsync(Stream bytes, CancellationToken ct = default);
            }
        }
        """;

    private static async Task InvokeTaskAsync(MethodInfo method, object target, params object?[] args)
    {
        try
        {
            var result = method.Invoke(target, args);
            await ((Task)result!).ConfigureAwait(false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
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
        var alc = new AssemblyLoadContext(
            "StreamingProxyPreCanceledToken_" + Guid.NewGuid(),
            isCollectible: false);
        return alc.LoadFromStream(ms);
    }

    private sealed class RecordingInvoker : ThrowingInvoker
    {
        private int _nextStreamId;

        public int ReserveCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int StreamedInvokeCount { get; private set; }

        public override RpcStreamHandle ReserveStream(RpcStreamKind kind)
        {
            ReserveCount++;
            return new RpcStreamHandle(Interlocked.Increment(ref _nextStreamId), kind);
        }

        public override void ReleaseStream(RpcStreamHandle handle) =>
            ReleaseCount++;

        public override Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            RpcStreamAttachment[] streams,
            CancellationToken ct = default)
        {
            StreamedInvokeCount++;
            return Task.FromCanceled<TResponse>(ct);
        }
    }

    private abstract class ThrowingInvoker : IRpcInvoker
    {
        public virtual RpcStreamHandle ReserveStream(RpcStreamKind kind) =>
            throw new NotSupportedException();

        public virtual void ReleaseStream(RpcStreamHandle handle) =>
            throw new NotSupportedException();

        public virtual Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            RpcStreamAttachment[] streams,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

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
