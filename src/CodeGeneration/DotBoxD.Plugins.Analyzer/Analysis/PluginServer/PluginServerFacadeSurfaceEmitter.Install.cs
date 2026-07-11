using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFacadeInstallSurfaceEmitter
{
    public static void Append(StringBuilder builder, PluginServerFacadeModel model)
    {
        AppendNoCaptureInvokeAsync(builder, model);
        AppendCaptureInvokeAsync(builder, model);
        AppendKernelAccessors(builder);
        AppendAnonymousKernelInstall(builder);
        AppendPackageInstallHelpers(builder);
        AppendLiveSettingHelpers(builder);
        AppendInstalledPackageGuards(builder);
    }

    private static void AppendNoCaptureInvokeAsync(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs and invokes a one-off server-side probe supplied as explicit generated IR.");
        builder.AppendLine(model.UserDefinesPublicInvokeAsync
            ? "    async global::System.Threading.Tasks.ValueTask<TReturn> global::DotBoxD.Abstractions.IPluginServer<" + model.WorldType + ">.InvokeAsync<TReturn>("
            : "    public async global::System.Threading.Tasks.ValueTask<TReturn> InvokeAsync<TReturn>(");
        builder.AppendLine("        global::System.Func<" + model.WorldType + ", global::System.Threading.Tasks.ValueTask<TReturn>> lambda,");
        builder.AppendLine("        [global::DotBoxD.Abstractions.IRBodyOf(nameof(lambda))] global::DotBoxD.Abstractions.IRInvocation<global::System.Func<" + model.WorldType + ", global::System.Threading.Tasks.ValueTask<TReturn>>, TReturn>? irInvocation = null,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        ThrowIfDisposed();");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(lambda);");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(irInvocation);");
        builder.AppendLine("        var __pluginId = await Services.EnsureAnonymousKernelAsync(irInvocation.PluginId, () => RequirePluginPackage(irInvocation.PackageFactory()), cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        var __request = irInvocation.EncodeArguments(lambda);");
        builder.AppendLine("        var __response = await Services.WireClient.InvokeServerExtensionAsync(__pluginId, __request, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("        return irInvocation.DecodeResult(lambda, __response);");
        builder.AppendLine("    }");
    }

    private static void AppendCaptureInvokeAsync(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs and invokes a one-off server-side probe with an explicit capture bag and generated IR.");
        builder.AppendLine(model.UserDefinesPublicInvokeAsync
            ? "    async global::System.Threading.Tasks.ValueTask<TReturn> global::DotBoxD.Abstractions.IPluginServer<" + model.WorldType + ">.InvokeAsync<TCaptures, TReturn>("
            : "    public async global::System.Threading.Tasks.ValueTask<TReturn> InvokeAsync<TCaptures, TReturn>(");
        builder.AppendLine("        TCaptures captures,");
        builder.AppendLine("        global::DotBoxD.Abstractions.RemoteServerInvocation<" + model.WorldType + ", TCaptures, TReturn> lambda,");
        builder.AppendLine("        [global::DotBoxD.Abstractions.IRBodyOf(nameof(lambda))] global::DotBoxD.Abstractions.IRInvocation<TCaptures, global::DotBoxD.Abstractions.RemoteServerInvocation<" + model.WorldType + ", TCaptures, TReturn>, TReturn>? irInvocation = null,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        where TCaptures : class");
        builder.AppendLine("    {");
        builder.AppendLine("        ThrowIfDisposed();");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(captures);");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(lambda);");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(irInvocation);");
        builder.AppendLine("        var __pluginId = await Services.EnsureAnonymousKernelAsync(irInvocation.PluginId, () => RequirePluginPackage(irInvocation.PackageFactory()), cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        var __request = irInvocation.EncodeArguments(captures, lambda);");
        builder.AppendLine("        var __response = await Services.WireClient.InvokeServerExtensionAsync(__pluginId, __request, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("        return irInvocation.DecodeResult(captures, lambda, __response);");
        builder.AppendLine("    }");
    }

    private static void AppendKernelAccessors(StringBuilder builder)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a live-settings handle for an installed kernel so the plugin can batch strongly typed setting updates.");
        builder.AppendLine("    public global::DotBoxD.Abstractions.ILiveSettingsHandle<TKernel> Get<TKernel>() where TKernel : class, new()");
        builder.AppendLine("    {");
        builder.AppendLine("        ThrowIfDisposed();");
        builder.AppendLine("        return new LiveSettingsHandle<TKernel>(this, global::DotBoxD.Plugins.Kernel.KernelPackageRegistry.Resolve<TKernel>().Manifest.PluginId);");
        builder.AppendLine("    }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Returns the installed plugin id for a server extension service type.");
        builder.AppendLine("    public string PluginId<TService>() where TService : class");
        builder.AppendLine("    {");
        builder.AppendLine("        ThrowIfDisposed();");
        builder.AppendLine("        return _serverExtensions.TryGetValue(typeof(TService), out var pluginId) ? pluginId : throw new global::System.InvalidOperationException($\"Server extension '{typeof(TService).FullName}' has not been registered.\");");
        builder.AppendLine("    }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Invokes an installed server extension kernel through the generated control-plane wire client.");
        builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        => RequireControl().InvokeServerExtensionAsync(pluginId, arguments, cancellationToken);");
    }

    private static void AppendAnonymousKernelInstall(StringBuilder builder)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs the package produced by the factory at most once and returns the installed plugin id.");
        builder.AppendLine("    public async global::System.Threading.Tasks.Task<string> EnsureAnonymousKernelAsync(string pluginId, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        ThrowIfDisposed();");
        builder.AppendLine("        while (true)");
        builder.AppendLine("        {");
        builder.AppendLine("            var created = false;");
        builder.AppendLine("            if (!_anonymousKernels.TryGetValue(pluginId, out var install))");
        builder.AppendLine("            {");
        builder.AppendLine("                install = CreateAnonymousKernelInstall(pluginId, factory);");
        builder.AppendLine("                if (!_anonymousKernels.TryAdd(pluginId, install))");
        builder.AppendLine("                {");
        builder.AppendLine("                    continue;");
        builder.AppendLine("                }");
        builder.AppendLine("                created = true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                return await AwaitAnonymousKernelAsync(pluginId, install, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch when (!created)");
        builder.AppendLine("            {");
        builder.AppendLine("                continue;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        AppendAnonymousKernelInstallHelpers(builder);
    }

    private static void AppendAnonymousKernelInstallHelpers(StringBuilder builder)
    {
        builder.AppendLine("    private global::System.Lazy<global::System.Threading.Tasks.Task<string>> CreateAnonymousKernelInstall(string pluginId, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.Lazy<global::System.Threading.Tasks.Task<string>>? install = null;");
        builder.AppendLine("        install = new global::System.Lazy<global::System.Threading.Tasks.Task<string>>(() => InstallAnonymousKernelAsync(pluginId, install!, factory));");
        builder.AppendLine("        return install;");
        builder.AppendLine("    }");
        builder.AppendLine("    private async global::System.Threading.Tasks.Task<string> InstallAnonymousKernelAsync(string pluginId, global::System.Lazy<global::System.Threading.Tasks.Task<string>> install, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory)");
        builder.AppendLine("    {");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            ThrowIfDisposed();");
        builder.AppendLine("            var installedId = await InstallServerExtensionPackageAsync(factory(), default).ConfigureAwait(false);");
        builder.AppendLine("            if (!global::System.StringComparer.Ordinal.Equals(installedId, pluginId))");
        builder.AppendLine("            {");
        builder.AppendLine("                RemoveAnonymousKernel(pluginId, install);");
        builder.AppendLine("                throw new global::System.InvalidOperationException($\"Anonymous kernel package id '{installedId}' did not match requested id '{pluginId}'.\");");
        builder.AppendLine("            }");
        builder.AppendLine("            return installedId;");
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            RemoveAnonymousKernel(pluginId, install);");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private async global::System.Threading.Tasks.Task<string> AwaitAnonymousKernelAsync(string pluginId, global::System.Lazy<global::System.Threading.Tasks.Task<string>> install, global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.Threading.Tasks.Task<string>? installTask = null;");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            ThrowIfDisposed();");
        builder.AppendLine("            installTask = install.Value;");
        builder.AppendLine("            var installedId = await installTask.WaitAsync(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            return installedId;");
        builder.AppendLine("        }");
        builder.AppendLine("        catch (global::System.OperationCanceledException) when (cancellationToken.IsCancellationRequested && installTask is not null && !installTask.IsCompleted)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            RemoveAnonymousKernel(pluginId, install);");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private bool RemoveAnonymousKernel(string pluginId, global::System.Lazy<global::System.Threading.Tasks.Task<string>> install)");
        builder.AppendLine("        => ((global::System.Collections.Generic.ICollection<global::System.Collections.Generic.KeyValuePair<string, global::System.Lazy<global::System.Threading.Tasks.Task<string>>>>)_anonymousKernels).Remove(new global::System.Collections.Generic.KeyValuePair<string, global::System.Lazy<global::System.Threading.Tasks.Task<string>>>(pluginId, install));");
    }

    private static void AppendPackageInstallHelpers(StringBuilder builder)
    {
        builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<string> InstallPluginPackageAsync(global::DotBoxD.Plugins.PluginPackage package, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        var pluginId = await RequireControl().InstallPluginAsync(global::DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Export(package), cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        RequireInstalledPackageId(package, pluginId);");
        builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("        MarkInstalled(package);");
        builder.AppendLine("        return pluginId;");
        builder.AppendLine("    }");
        builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<string> InstallSubscriptionPackageAsync(global::DotBoxD.Plugins.PluginPackage package, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        var pluginId = await RequireControl().InstallSubscriptionAsync(global::DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Export(package), cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        RequireInstalledPackageId(package, pluginId);");
        builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("        MarkInstalled(package);");
        builder.AppendLine("        return pluginId;");
        builder.AppendLine("    }");
        builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<string> InstallServerExtensionPackageAsync(global::DotBoxD.Plugins.PluginPackage package, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        var pluginId = await RequireControl().InstallServerExtensionAsync(global::DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Export(package), cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        RequireInstalledPackageId(package, pluginId);");
        builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("        MarkInstalled(package);");
        builder.AppendLine("        return pluginId;");
        builder.AppendLine("    }");
        builder.AppendLine("    private void MarkInstalled(global::DotBoxD.Plugins.PluginPackage package)");
        builder.AppendLine("    {");
        builder.AppendLine("        _installedPluginIds.Add(package.Manifest.PluginId);");
        builder.AppendLine("        RecordLiveSettingDefaults(package);");
        builder.AppendLine("    }");
    }

    private static void AppendLiveSettingHelpers(StringBuilder builder)
    {
        builder.AppendLine("    private void RecordLiveSettingDefaults(global::DotBoxD.Plugins.PluginPackage package)");
        builder.AppendLine("    {");
        builder.AppendLine("        var values = new global::System.Collections.Generic.Dictionary<string, object?>(global::System.StringComparer.Ordinal);");
        builder.AppendLine("        foreach (var setting in package.Manifest.LiveSettings)");
        builder.AppendLine("        {");
        builder.AppendLine("            values[setting.Name] = setting.DefaultValue;");
        builder.AppendLine("        }");
        builder.AppendLine("        lock (_liveSettingsGate)");
        builder.AppendLine("        {");
        builder.AppendLine("        _liveSettingValues[package.Manifest.PluginId] = values;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private global::System.Collections.Generic.Dictionary<string, object?> SnapshotLiveSettingValues(string pluginId)");
        builder.AppendLine("    {");
        builder.AppendLine("        lock (_liveSettingsGate)");
        builder.AppendLine("        {");
        builder.AppendLine("        return _liveSettingValues.TryGetValue(pluginId, out var values)");
        builder.AppendLine("            ? new global::System.Collections.Generic.Dictionary<string, object?>(values, global::System.StringComparer.Ordinal)");
        builder.AppendLine("            : new global::System.Collections.Generic.Dictionary<string, object?>(global::System.StringComparer.Ordinal);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private void RecordLiveSettingValue(string pluginId, string name, object? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        lock (_liveSettingsGate)");
        builder.AppendLine("        {");
        builder.AppendLine("        if (!_liveSettingValues.TryGetValue(pluginId, out var values))");
        builder.AppendLine("        {");
        builder.AppendLine("            values = new global::System.Collections.Generic.Dictionary<string, object?>(global::System.StringComparer.Ordinal);");
        builder.AppendLine("            _liveSettingValues[pluginId] = values;");
        builder.AppendLine("        }");
        builder.AppendLine("        values[name] = value;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void AppendInstalledPackageGuards(StringBuilder builder)
    {
        builder.AppendLine("    private static void RequireInstalledPackageId(global::DotBoxD.Plugins.PluginPackage package, string pluginId)");
        builder.AppendLine("    {");
        builder.AppendLine("        var manifestId = package.Manifest.PluginId;");
        builder.AppendLine("        if (global::System.StringComparer.Ordinal.Equals(pluginId, manifestId))");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine("        var callbackId = package.CallbackSubscriptionId;");
        builder.AppendLine("        if (callbackId is not null && global::System.StringComparer.Ordinal.Equals(pluginId, callbackId))");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine("        throw new global::System.InvalidOperationException($\"Installed package id '{pluginId}' did not match manifest id '{manifestId}'.\");");
        builder.AppendLine("    }");
        builder.AppendLine("    private void RequireInstalledKernel<TKernel>(string pluginId)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!_installedPluginIds.Contains(pluginId))");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.InvalidOperationException($\"Kernel '{typeof(TKernel).FullName}' has not been installed.\");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private static global::DotBoxD.Plugins.PluginPackage RequirePluginPackage(object package)");
        builder.AppendLine("        => package as global::DotBoxD.Plugins.PluginPackage ??");
        builder.AppendLine("            throw new global::System.InvalidOperationException(\"Generated InvokeAsync IR did not provide a plugin package.\");");
    }
}
