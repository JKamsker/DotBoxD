using System.Reflection;
using System.Runtime.Loader;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

internal static class GeneratedRoundTripTestSupport
{
    internal sealed class Harness
    {
        private readonly object _proxy;
        private readonly object _impl;
        private readonly Type _interfaceType;

        public Assembly Assembly { get; }
        public IServiceDispatcher Dispatcher { get; }
        public ISerializer Serializer { get; }

        private Harness(
            Assembly assembly,
            object proxy,
            object impl,
            IServiceDispatcher dispatcher,
            ISerializer serializer,
            Type interfaceType)
        {
            Assembly = assembly;
            _proxy = proxy;
            _impl = impl;
            Dispatcher = dispatcher;
            Serializer = serializer;
            _interfaceType = interfaceType;
        }

        public static Harness Build(string source, string interfaceFqn, string implFqn)
        {
            var asm = CompileAndLoad(source);
            var serializer = new TestJsonSerializer();
            var registry = new InstanceRegistry();
            var client = new LoopbackClient(serializer, registry);
            return Attach(asm, client, interfaceFqn, implFqn, serializer, registry);
        }

        public static Harness Attach(
            Assembly asm,
            LoopbackClient client,
            string interfaceFqn,
            string implFqn,
            ISerializer serializer,
            InstanceRegistry registry)
        {
            var interfaceType = asm.GetType(interfaceFqn)
                ?? throw new InvalidOperationException($"interface {interfaceFqn} not found in generated assembly");
            var implType = asm.GetType(implFqn)
                ?? throw new InvalidOperationException($"impl {implFqn} not found in generated assembly");

            var impl = Activator.CreateInstance(implType)!;
            var dispatcherType = FindGenerated(asm, "Dispatcher", t =>
                t.GetConstructors().Any(c =>
                {
                    var p = c.GetParameters();
                    return p.Length == 1 && p[0].ParameterType == interfaceType;
                }));
            var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType, impl)!;
            client.Register(dispatcher);

            var proxyType = FindGenerated(asm, "Proxy", interfaceType.IsAssignableFrom);
            var proxy = Activator.CreateInstance(proxyType, client)!;

            return new Harness(asm, proxy, impl, dispatcher, serializer, interfaceType);
        }

        public async Task<object?> CallAsync(string method, params object?[] args)
        {
            var interfaceMethod = _interfaceType.GetMethod(method)
                ?? throw new InvalidOperationException($"interface has no method {method}");
            var parameterTypes = interfaceMethod.GetParameters().Select(p => p.ParameterType).ToArray();

            if (parameterTypes.Length == args.Length + 1 && parameterTypes[^1] == typeof(CancellationToken))
            {
                args = args.Append((object)CancellationToken.None).ToArray();
            }

            var proxyMethod = _proxy.GetType().GetMethod(method, parameterTypes)
                ?? throw new InvalidOperationException($"proxy has no method {method} with the interface signature");

            var result = proxyMethod.Invoke(_proxy, args);
            return await AwaitDynamic(result, proxyMethod.ReturnType);
        }

        public Type LoadType(string fqn) =>
            Assembly.GetType(fqn) ?? throw new InvalidOperationException($"type {fqn} not found");

        public object? GetImplProperty(string name) =>
            _impl.GetType().GetProperty(name)!.GetValue(_impl);

        private static Type FindGenerated(Assembly asm, string suffix, Func<Type, bool> predicate) =>
            asm.GetTypes().Single(t =>
                t.IsClass && !t.IsAbstract && t.Name.EndsWith(suffix, StringComparison.Ordinal) && predicate(t));
    }

    internal static int ReadInt(object instance, string property) =>
        (int)instance.GetType().GetProperty(property)!.GetValue(instance)!;

    internal static Assembly CompileAndLoad(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generator must not report errors for a supported service");

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            var dump = string.Join("\n\n----\n\n",
                runResult.GeneratedTrees.Select(t => t.FilePath + "\n" + t.GetText()));
            throw new InvalidOperationException("Emit failed:\n" + errors + "\n\nGenerated:\n" + dump);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("RoundTripTest_" + Guid.NewGuid(), isCollectible: false);
        return alc.LoadFromStream(ms);
    }

    private static async Task<object?> AwaitDynamic(object? result, Type returnType)
    {
        switch (result)
        {
            case null:
                return null;
            case Task task:
                await task.ConfigureAwait(false);
                return returnType.IsGenericType ? returnType.GetProperty("Result")!.GetValue(task) : null;
            default:
                var runtimeType = result.GetType();
                if (runtimeType == typeof(ValueTask))
                {
                    await ((ValueTask)result).ConfigureAwait(false);
                    return null;
                }

                if (runtimeType.IsGenericType && runtimeType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                {
                    var asTask = (Task)runtimeType.GetMethod("AsTask")!.Invoke(result, null)!;
                    await asTask.ConfigureAwait(false);
                    return asTask.GetType().GetProperty("Result")!.GetValue(asTask);
                }

                return result;
        }
    }

    internal sealed class LoopbackClient(ISerializer serializer, IInstanceRegistry registry) : IRpcInvoker
    {
        private readonly Dictionary<string, IServiceDispatcher> _dispatchers = new(StringComparer.Ordinal);

        public bool IsConnected => true;

        public void Register(IServiceDispatcher dispatcher) =>
            _dispatchers[dispatcher.ServiceName] = dispatcher;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;

        public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default)
        {
            using var p = serializer.SerializeToPayload(request);
            using var reply = await Resolve(service).DispatchToPayloadAsync(method, p.Memory, serializer, registry, ct);
            return serializer.Deserialize<TResponse>(reply.Memory);
        }

        public async Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default)
        {
            using var reply = await Resolve(service).DispatchToPayloadAsync(
                method,
                ReadOnlyMemory<byte>.Empty,
                serializer,
                registry,
                ct);
            return serializer.Deserialize<TResponse>(reply.Memory);
        }

        public async Task InvokeAsync<TRequest>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default)
        {
            using var p = serializer.SerializeToPayload(request);
            using var reply = await Resolve(service).DispatchToPayloadAsync(method, p.Memory, serializer, registry, ct);
        }

        public async Task InvokeAsync(string service, string method, CancellationToken ct = default)
        {
            using var reply = await Resolve(service).DispatchToPayloadAsync(
                method,
                ReadOnlyMemory<byte>.Empty,
                serializer,
                registry,
                ct);
        }

        public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default)
        {
            using var p = serializer.SerializeToPayload(request);
            using var reply = await Resolve(service).DispatchOnInstanceToPayloadAsync(
                instanceId,
                method,
                p.Memory,
                serializer,
                registry,
                ct);
            return serializer.Deserialize<TResponse>(reply.Memory);
        }

        public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default)
        {
            using var reply = await Resolve(service).DispatchOnInstanceToPayloadAsync(
                instanceId,
                method,
                ReadOnlyMemory<byte>.Empty,
                serializer,
                registry,
                ct);
            return serializer.Deserialize<TResponse>(reply.Memory);
        }

        public async Task InvokeOnInstanceAsync<TRequest>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default)
        {
            using var p = serializer.SerializeToPayload(request);
            using var reply = await Resolve(service).DispatchOnInstanceToPayloadAsync(
                instanceId,
                method,
                p.Memory,
                serializer,
                registry,
                ct);
        }

        public async Task InvokeOnInstanceAsync(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default)
        {
            using var reply = await Resolve(service).DispatchOnInstanceToPayloadAsync(
                instanceId,
                method,
                ReadOnlyMemory<byte>.Empty,
                serializer,
                registry,
                ct);
        }

        private IServiceDispatcher Resolve(string service) =>
            _dispatchers.TryGetValue(service, out var dispatcher)
                ? dispatcher
                : throw new InvalidOperationException($"no dispatcher registered for service '{service}'");
    }
}
