namespace DotBoxD.Kernels.Game.Plugin.Client;

using System.Globalization;
using System.Reflection;

/// <summary>
/// Example-local facade that gives the plugin a server-shaped surface
/// (<c>server.Kernels.Register&lt;TService, TKernel&gt;()</c>,
/// <c>server.Kernels.Get&lt;TKernel&gt;().SetValuesAsync(..)</c>) while forwarding over the unchanged
/// <see cref="IGamePluginControlService"/> IPC contract. It resolves each kernel's analyzer-generated
/// package by type (via <see cref="KernelPackageRegistry"/>) and ships it as verified IR.
/// </summary>
internal sealed class RemotePluginServer
{
    private readonly IGamePluginControlService _control;

    public RemotePluginServer(IGamePluginControlService control)
    {
        _control = control;
        Kernels = new RemoteKernelControl(control);
        KernelRpc = new RemoteKernelRpcControl(control);
        World = new RemoteWorldControl(control);
    }

    public RemoteKernelControl Kernels { get; }

    public RemoteKernelRpcControl KernelRpc { get; }

    public RemoteWorldControl World { get; }

    /// <summary>Holds the connection open until the server completes its with-plugin phase.</summary>
    public ValueTask HoldUntilShutdownAsync() => _control.HoldUntilShutdownAsync();
}

internal sealed class RemoteKernelControl
{
    private readonly IGamePluginControlService _control;

    public RemoteKernelControl(IGamePluginControlService control) => _control = control;

    /// <summary>
    /// Registers a kernel as the implementation of a server service contract: resolves its generated
    /// verified-IR package and ships it. Returns the installed plugin id.
    /// </summary>
    public async ValueTask<string> Register<TService, TKernel>()
        where TService : class
        where TKernel : class, TService
    {
        var json = PluginPackageJsonSerializer.Export(KernelPackageRegistry.Resolve<TKernel>());
        return await _control.InstallPluginAsync(json).ConfigureAwait(false);
    }

    /// <summary>Strongly-typed settings handle for a kernel this plugin authored.</summary>
    public RemoteKernelHandle<TKernel> Get<TKernel>() where TKernel : class, new()
        => new(_control, PluginId(typeof(TKernel)));

    internal static string PluginId(Type kernelType)
        => kernelType.GetCustomAttribute<PluginAttribute>()?.Id
           ?? throw new InvalidOperationException($"Kernel '{kernelType.FullName}' has no [Plugin] id.");
}

internal sealed class RemoteKernelRpcControl
{
    private readonly IGamePluginControlService _control;
    private readonly Dictionary<Type, string> _services = new();

    public RemoteKernelRpcControl(IGamePluginControlService control) => _control = control;

    public async ValueTask<string> Register<TService, TKernel>()
        where TService : class
        where TKernel : class
    {
        var json = PluginPackageJsonSerializer.Export(KernelPackageRegistry.Resolve<TKernel>());
        var pluginId = await _control.InstallKernelRpcAsync(json).ConfigureAwait(false);
        _services[typeof(TService)] = pluginId;
        return pluginId;
    }

    public TService Get<TService>() where TService : class
    {
        if (!_services.TryGetValue(typeof(TService), out var pluginId))
        {
            throw new InvalidOperationException(
                $"No kernel RPC service is registered for '{typeof(TService)}'. Call Register first.");
        }

        return RemoteKernelRpcServiceProxy.Create<TService>(_control, pluginId);
    }
}

internal sealed class RemoteWorldControl
{
    private readonly IGamePluginControlService _control;

    public RemoteWorldControl(IGamePluginControlService control) => _control = control;

    public ValueTask<bool> KillMonsterAsync(string monsterId)
        => _control.KillMonsterAsync(monsterId);

    public ValueTask<bool> IsMonsterAsync(string entityId)
        => _control.IsMonsterAsync(entityId);

    public ValueTask<int> GetHealthAsync(string entityId)
        => _control.GetEntityHealthAsync(entityId);

    public ValueTask<int> GetLevelAsync(string entityId)
        => _control.GetEntityLevelAsync(entityId);

    public ValueTask<int> GetPositionAsync(string entityId)
        => _control.GetEntityPositionAsync(entityId);
}

internal class RemoteKernelRpcServiceProxy : DispatchProxy
{
    private IGamePluginControlService _control = null!;
    private string _pluginId = string.Empty;

    public static TService Create<TService>(IGamePluginControlService control, string pluginId)
        where TService : class
    {
        var proxy = Create<TService, RemoteKernelRpcServiceProxy>();
        var typed = (RemoteKernelRpcServiceProxy)(object)proxy!;
        typed._control = control;
        typed._pluginId = pluginId;
        return proxy!;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            throw new NotSupportedException("Kernel RPC service proxy received a null method.");
        }

        var parameters = targetMethod.GetParameters();
        var arguments = new KernelRpcWireValue[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var sandbox = KernelRpcMarshaller.ToSandboxValue(args?[i], parameters[i].ParameterType);
            arguments[i] = KernelRpcWireValueConverter.FromSandboxValue(sandbox);
        }

        var returnType = targetMethod.ReturnType;
        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            var inner = returnType.GetGenericArguments()[0];
            if (definition == typeof(Task<>))
            {
                return InvokeGeneric(nameof(InvokeTaskAsync), inner, arguments);
            }

            if (definition == typeof(ValueTask<>))
            {
                return InvokeGeneric(nameof(InvokeValueTaskAsync), inner, arguments);
            }
        }

        return InvokeRemoteAsync(returnType, arguments).AsTask().GetAwaiter().GetResult();
    }

    private object InvokeGeneric(string methodName, Type resultType, KernelRpcWireValue[] arguments)
        => typeof(RemoteKernelRpcServiceProxy)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType)
            .Invoke(this, [arguments])!;

    private async Task<T> InvokeTaskAsync<T>(KernelRpcWireValue[] arguments)
        => (T)(await InvokeRemoteAsync(typeof(T), arguments).ConfigureAwait(false))!;

    private async ValueTask<T> InvokeValueTaskAsync<T>(KernelRpcWireValue[] arguments)
        => (T)(await InvokeRemoteAsync(typeof(T), arguments).ConfigureAwait(false))!;

    private async ValueTask<object?> InvokeRemoteAsync(Type returnType, KernelRpcWireValue[] arguments)
    {
        var result = await _control.InvokeKernelRpcAsync(_pluginId, arguments).ConfigureAwait(false);
        return ToClr(result, returnType);
    }

    private static object? ToClr(KernelRpcWireValue result, Type type)
    {
        var sandboxType = KernelRpcMarshaller.SandboxTypeOf(type);
        var sandbox = KernelRpcWireValueConverter.ToSandboxValue(result, sandboxType);
        return KernelRpcMarshaller.FromSandboxValue(sandbox, type);
    }
}

internal sealed class RemoteKernelHandle<TKernel> where TKernel : class, new()
{
    private readonly IGamePluginControlService _control;
    private readonly string _pluginId;

    public RemoteKernelHandle(IGamePluginControlService control, string pluginId)
    {
        _control = control;
        _pluginId = pluginId;
    }

    /// <summary>
    /// Sets live setting values from a typed lambda. The lambda mutates a local draft; the resulting
    /// <c>[LiveSetting]</c> values are shipped over IPC. (For read-modify-write against live server
    /// state, do it server-side under the kernel's execution gate.)
    /// </summary>
    public ValueTask SetValuesAsync(Action<TKernel> set, bool atomic = false)
    {
        ArgumentNullException.ThrowIfNull(set);
        var draft = new TKernel();
        set(draft);

        var updates = typeof(TKernel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is not null && p.GetCustomAttribute<LiveSettingAttribute>() is not null)
            .Select(p => new LiveSettingUpdate(
                p.Name,
                Convert.ToString(p.GetValue(draft), CultureInfo.InvariantCulture) ?? string.Empty))
            .ToArray();

        return _control.UpdateSettingsAsync(_pluginId, updates, atomic);
    }
}
