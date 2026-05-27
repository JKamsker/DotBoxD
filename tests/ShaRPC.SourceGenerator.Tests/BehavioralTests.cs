using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Attributes;
using ShaRPC.Core.Client;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Compiles and emits an assembly using the generated proxy/dispatcher and then
/// exercises them via reflection. Proves the generated code is wire-compatible
/// with the IShaRpcClient / IServiceDispatcher contracts.
/// </summary>
public class BehavioralTests
{
    private const string ServiceSource = """
        using ShaRPC.Core.Attributes;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Behavior.Demo
        {
            [ShaRpcService]
            public interface IMath
            {
                Task<int> AddAsync(int a, int b, CancellationToken ct = default);
                Task<int> SquareAsync(int x, CancellationToken ct = default);
                Task PingAsync(CancellationToken ct = default);
            }
        }
        """;

    [Fact]
    public async Task GeneratedProxy_ForwardsInvocationToShaRpcClient_WithExpectedServiceAndMethodNames()
    {
        var (assembly, _) = CompileWithGenerator(ServiceSource);

        var proxyType = assembly.GetType("Behavior.Demo.MathProxy");
        proxyType.Should().NotBeNull("the generator should emit Behavior.Demo.MathProxy");

        var mockClient = new RecordingClient();
        mockClient.NextResultObject = 42;

        var proxy = Activator.CreateInstance(proxyType!, mockClient)!;

        var interfaceType = assembly.GetType("Behavior.Demo.IMath")!;
        var addMethod = interfaceType.GetMethod("AddAsync")!;

        var resultTask = (Task)addMethod.Invoke(proxy, new object[] { 2, 3, CancellationToken.None })!;
        await resultTask;
        var result = (int)resultTask.GetType().GetProperty("Result")!.GetValue(resultTask)!;

        result.Should().Be(42);
        mockClient.LastService.Should().Be("IMath");
        mockClient.LastMethod.Should().Be("AddAsync");
    }

    [Fact]
    public async Task GeneratedDispatcher_DeserializesArgumentsAndInvokesServiceImplementation()
    {
        var (assembly, _) = CompileWithGenerator(ServiceSource);

        var dispatcherType = assembly.GetType("Behavior.Demo.MathDispatcher");
        dispatcherType.Should().NotBeNull("the generator should emit Behavior.Demo.MathDispatcher");

        var interfaceType = assembly.GetType("Behavior.Demo.IMath")!;
        var impl = new MathImpl();
        var implProxy = DispatchTargetFactory.CreateProxy(interfaceType, impl);

        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType!, implProxy)!;
        dispatcher.ServiceName.Should().Be("IMath");

        var serializer = new JsonSerializerWrapper();

        // Two-arg method goes through tuple deserialization.
        var payload = serializer.Serialize<ValueTuple<int, int>>(new ValueTuple<int, int>(7, 5));
        var bytes = await dispatcher.DispatchAsync("AddAsync", payload, serializer, CancellationToken.None);
        var sum = serializer.Deserialize<int>(bytes);
        sum.Should().Be(12);
        impl.LastCall.Should().Be("Add(7,5)");

        // One-arg method goes through single deserialization.
        var single = serializer.Serialize<int>(6);
        var bytes2 = await dispatcher.DispatchAsync("SquareAsync", single, serializer, CancellationToken.None);
        var sq = serializer.Deserialize<int>(bytes2);
        sq.Should().Be(36);

        // Zero-arg, void-returning method returns empty payload.
        var pingBytes = await dispatcher.DispatchAsync("PingAsync", Array.Empty<byte>(), serializer, CancellationToken.None);
        pingBytes.Should().BeEmpty();
        impl.PingCount.Should().Be(1);
    }

    [Fact]
    public void GeneratedExtensions_ExposeCreateProxyAndAddExtensionMethods()
    {
        var (assembly, _) = CompileWithGenerator(ServiceSource);

        var extType = assembly.GetType("ShaRPC.Generated.ShaRpcGeneratedExtensions");
        extType.Should().NotBeNull();

        var createMethod = extType!.GetMethod("CreateMathProxy", BindingFlags.Public | BindingFlags.Static);
        createMethod.Should().NotBeNull();
        createMethod!.GetParameters().Should().ContainSingle().Which.ParameterType.Should().Be(typeof(IShaRpcClient));

        var addMethod = extType.GetMethod("AddMath", BindingFlags.Public | BindingFlags.Static);
        addMethod.Should().NotBeNull();
        addMethod!.GetParameters().Should().HaveCount(2);
        addMethod.GetParameters()[0].ParameterType.Should().Be(typeof(ShaRpcServerBuilder));
    }

    // ---------- compilation infrastructure ----------

    private static (Assembly Assembly, string GeneratedDump) CompileWithGenerator(string source)
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
            var errors = string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            var dump = string.Join("\n\n----\n\n",
                runResult.GeneratedTrees.Select(t => t.FilePath + "\n" + t.GetText().ToString()));
            throw new InvalidOperationException("Emit failed:\n" + errors + "\n\nGenerated:\n" + dump);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("BehavioralTest_" + Guid.NewGuid(), isCollectible: false);
        var asm = alc.LoadFromStream(ms);

        var generatedDump = string.Join("\n\n",
            runResult.GeneratedTrees.Select(t => t.GetText().ToString()));
        return (asm, generatedDump);
    }

    // ---------- test doubles ----------

    private sealed class RecordingClient : IShaRpcClient
    {
        public string? LastService { get; private set; }
        public string? LastMethod { get; private set; }
        public object? LastRequest { get; private set; }
        public object? NextResultObject { get; set; }

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            LastMethod = method;
            LastRequest = request;
            return Task.FromResult((TResponse)NextResultObject!);
        }

        public Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default)
        {
            LastService = service;
            LastMethod = method;
            return Task.FromResult((TResponse)NextResultObject!);
        }

        public Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            LastMethod = method;
            LastRequest = request;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }

    private sealed class JsonSerializerWrapper : ISerializer
    {
        private static readonly JsonSerializerOptions s_options = new() { IncludeFields = true };

        public byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, s_options);

        public T Deserialize<T>(ReadOnlySpan<byte> data) => JsonSerializer.Deserialize<T>(data, s_options)!;

        public object? Deserialize(ReadOnlySpan<byte> data, Type type) => JsonSerializer.Deserialize(data, type, s_options);
    }

    // Implementation of the in-memory IMath service. We can't statically reference the type
    // because it lives in the dynamically compiled assembly, so we use a dispatch proxy.
    public sealed class MathImpl
    {
        public string? LastCall { get; private set; }
        public int PingCount { get; private set; }

        public Task<int> AddAsync(int a, int b, CancellationToken ct)
        {
            LastCall = $"Add({a},{b})";
            return Task.FromResult(a + b);
        }

        public Task<int> SquareAsync(int x, CancellationToken ct)
        {
            LastCall = $"Square({x})";
            return Task.FromResult(x * x);
        }

        public Task PingAsync(CancellationToken ct)
        {
            PingCount++;
            return Task.CompletedTask;
        }
    }

    private static class DispatchTargetFactory
    {
        // DispatchProxy.Create<TInterface, TProxy>() is a generic method.
        // We need to make it generic over (dynamicInterfaceType, MathDispatchProxy) at runtime.
        public static object CreateProxy(Type interfaceType, MathImpl impl)
        {
            // Find DispatchProxy.Create<T,TProxy>() - the generic static factory with two type params.
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(interfaceType, typeof(MathDispatchProxy));
            var proxy = closed.Invoke(null, null)!;
            ((MathDispatchProxy)proxy).Impl = impl;
            return proxy;
        }
    }

    public class MathDispatchProxy : DispatchProxy
    {
        public MathImpl? Impl;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            switch (targetMethod.Name)
            {
                case "AddAsync":
                    return Impl!.AddAsync((int)args![0]!, (int)args[1]!, (CancellationToken)args[2]!);
                case "SquareAsync":
                    return Impl!.SquareAsync((int)args![0]!, (CancellationToken)args[1]!);
                case "PingAsync":
                    return Impl!.PingAsync((CancellationToken)args![0]!);
                default:
                    throw new InvalidOperationException("unsupported method: " + targetMethod.Name);
            }
        }
    }
}
