using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime.Rpc;

/// <summary>
/// A runtime proxy that implements a server-extension interface by marshaling each call through an
/// installed batch kernel. Arguments are converted to sandbox values, the verified IR runs request/
/// response via <see cref="InstalledKernel.InvokeServerExtensionAsync"/>, and the result is marshaled
/// back to the method's return type, so
/// <c>server.ServerExtension&lt;IMonsterKiller&gt;().KillMonsters(ids)</c> returns real C# objects.
/// The service is expected to declare a single batch method; synchronous, <c>Task&lt;T&gt;</c>, and
/// <c>ValueTask&lt;T&gt;</c> return shapes are supported.
/// </summary>
public class ServerExtensionProxy : DispatchProxy
{
    private static readonly ConcurrentDictionary<MethodInfo, ServerExtensionMethod> MethodCache = new();
    private static readonly MethodInfo BoxTaskAsyncMethod =
        typeof(ServerExtensionProxy).GetMethod(nameof(BoxTaskAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo BoxValueTaskAsyncMethod =
        typeof(ServerExtensionProxy).GetMethod(nameof(BoxValueTaskAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    private InstalledKernel _kernel = null!;

    public static TService Create<TService>(InstalledKernel kernel) where TService : class
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ServerExtensionProxyValidation.ValidateServiceContract(typeof(TService));
        var proxy = Create<TService, ServerExtensionProxy>();
        ((ServerExtensionProxy)(object)proxy!)._kernel = kernel;
        return proxy!;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            throw new NotSupportedException("Server extension proxy received a null method.");
        }

        var method = MethodCache.GetOrAdd(targetMethod, static target => new ServerExtensionMethod(target));
        var cancellationToken = method.CancellationToken(args);
        cancellationToken.ThrowIfCancellationRequested();
        PluginKernelRevocation.ThrowIfRevoked(_kernel.IsRevoked);
        var arguments = ConvertPayloadArguments(args, method.PayloadParameterTypes);

        return method.Materialize(_kernel.InvokeServerExtensionAsync(arguments, cancellationToken));
    }

    private static SandboxValue[] ConvertPayloadArguments(object?[]? args, Type[] payloadParameterTypes)
    {
        if (payloadParameterTypes.Length == 0)
        {
            return Array.Empty<SandboxValue>();
        }

        var arguments = new SandboxValue[payloadParameterTypes.Length];
        for (var i = 0; i < payloadParameterTypes.Length; i++)
        {
            arguments[i] = KernelRpcMarshaller.ToSandboxValue(args?[i], payloadParameterTypes[i]);
        }

        return arguments;
    }

    private static Func<ValueTask<SandboxValue>, object?> CreateMaterializer(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return pending =>
            {
                ConsumeUnit(pending.AsTask().GetAwaiter().GetResult());
                return null;
            };
        }

        if (returnType == typeof(Task))
        {
            return pending => InvokeTaskAsync(pending);
        }

        if (returnType == typeof(ValueTask))
        {
            return pending => InvokeValueTaskAsync(pending);
        }

        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            if (definition == typeof(Task<>))
            {
                var inner = returnType.GetGenericArguments()[0];
                return (Func<ValueTask<SandboxValue>, object?>)BoxTaskAsyncMethod
                    .MakeGenericMethod(inner)
                    .CreateDelegate(typeof(Func<ValueTask<SandboxValue>, object?>));
            }

            if (definition == typeof(ValueTask<>))
            {
                var inner = returnType.GetGenericArguments()[0];
                return (Func<ValueTask<SandboxValue>, object?>)BoxValueTaskAsyncMethod
                    .MakeGenericMethod(inner)
                    .CreateDelegate(typeof(Func<ValueTask<SandboxValue>, object?>));
            }
        }

        return pending =>
        {
            // Synchronous return shape: block on the ValueTask directly. InvokeServerExtensionAsync uses
            // the default non-pooled ValueTask shape here; AsTask() only allocated a throwaway Task<T>.
            // Direct GetResult() is valid for this path's single consumption and unwraps exceptions
            // identically (no AggregateException).
            var result = pending.GetAwaiter().GetResult();
            return KernelRpcMarshaller.FromSandboxValue(result, returnType);
        };
    }

    private static bool IsCancellationToken(Type type)
        => type == typeof(CancellationToken);

    private static object BoxTaskAsync<T>(ValueTask<SandboxValue> pending)
        => InvokeTaskAsync<T>(pending);

    private static object BoxValueTaskAsync<T>(ValueTask<SandboxValue> pending)
        => InvokeValueTaskAsync<T>(pending);

    private static async Task InvokeTaskAsync(ValueTask<SandboxValue> pending)
        => ConsumeUnit(await pending.ConfigureAwait(false));

    private static async ValueTask InvokeValueTaskAsync(ValueTask<SandboxValue> pending)
        => ConsumeUnit(await pending.ConfigureAwait(false));

    private static void ConsumeUnit(SandboxValue value)
    {
        if (value.Type != SandboxType.Unit)
        {
            throw new NotSupportedException(
                $"Server extension value expected '{SandboxType.Unit}' but received '{value.Type}'.");
        }
    }

    private sealed class ServerExtensionMethod
    {
        private readonly int _cancellationTokenIndex;
        private readonly Func<ValueTask<SandboxValue>, object?> _materializer;

        public ServerExtensionMethod(MethodInfo method)
        {
            var parameters = method.GetParameters();
            _cancellationTokenIndex = parameters.Length > 0 &&
                IsCancellationToken(parameters[^1].ParameterType)
                    ? parameters.Length - 1
                    : -1;

            var payloadParameterCount = _cancellationTokenIndex >= 0
                ? _cancellationTokenIndex
                : parameters.Length;
            PayloadParameterTypes = new Type[payloadParameterCount];
            for (var i = 0; i < payloadParameterCount; i++)
            {
                PayloadParameterTypes[i] = parameters[i].ParameterType;
            }

            _materializer = CreateMaterializer(method.ReturnType);
        }

        public Type[] PayloadParameterTypes { get; }

        public CancellationToken CancellationToken(object?[]? args)
        {
            if (_cancellationTokenIndex < 0 ||
                args is null ||
                args.Length <= _cancellationTokenIndex ||
                args[_cancellationTokenIndex] is not CancellationToken cancellationToken)
            {
                return default;
            }

            return cancellationToken;
        }

        public object? Materialize(ValueTask<SandboxValue> pending)
            => _materializer(pending);
    }

    private static async Task<T> InvokeTaskAsync<T>(ValueTask<SandboxValue> pending)
    {
        var result = await pending.ConfigureAwait(false);
        return (T)KernelRpcMarshaller.FromSandboxValue(result, typeof(T))!;
    }

    private static async ValueTask<T> InvokeValueTaskAsync<T>(ValueTask<SandboxValue> pending)
    {
        var result = await pending.ConfigureAwait(false);
        return (T)KernelRpcMarshaller.FromSandboxValue(result, typeof(T))!;
    }
}
