namespace SafeIR.Game.Server;

using System.Diagnostics;

/// <summary>
/// Launches the plugin host child process and forwards its stdout/stderr so the demo shows the
/// host's local preview and ship logs inline. Resolves the host dll for both <c>dotnet run</c>
/// (dev) and <c>--no-build</c> smoke runs.
/// </summary>
internal static class PluginHostLauncher
{
    private const string HostDllEnvVar = "SAFEIR_GAME_PLUGINHOST_DLL";
    private const string HostProjectDir = "SafeIR.Game.PluginHost";
    private const string ServerProjectDir = "SafeIR.Game.Server";
    private const string HostDllName = "SafeIR.Game.PluginHost.dll";

    public static Process Launch(string pipeName)
    {
        var hostDll = ResolveHostDll();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(hostDll);
        startInfo.ArgumentList.Add(pipeName);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => ForwardLine(Console.Out, e.Data);
        process.ErrorDataReceived += (_, e) => ForwardLine(Console.Error, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void ForwardLine(TextWriter writer, string? line)
    {
        if (line is not null)
        {
            writer.WriteLine(line);
        }
    }

    private static string ResolveHostDll()
    {
        var configured = Environment.GetEnvironmentVariable(HostDllEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
            {
                throw new FileNotFoundException(
                    $"{HostDllEnvVar} points to a missing host dll: {configured}");
            }

            return configured;
        }

        // Server base dir: .../examples/GameServer/SafeIR.Game.Server/bin/<Config>/net10.0/
        // Sibling host:    .../examples/GameServer/SafeIR.Game.PluginHost/bin/<Config>/net10.0/<dll>
        var serverBase = AppContext.BaseDirectory;
        var candidate = SiblingHostPath(serverBase);
        if (candidate is not null && File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException(
            "Could not resolve the plugin host dll. Build the solution or set " +
            $"{HostDllEnvVar} to {HostDllName}. Looked beside: {serverBase}");
    }

    private static string? SiblingHostPath(string serverBaseDirectory)
    {
        var trimmed = serverBaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        // trimmed -> .../SafeIR.Game.Server/bin/<Config>/net10.0
        var tfm = Path.GetFileName(trimmed);
        var configDir = Path.GetDirectoryName(trimmed);
        var binDir = Path.GetDirectoryName(configDir);
        var serverProjectDir = Path.GetDirectoryName(binDir);
        if (configDir is null || binDir is null || serverProjectDir is null)
        {
            return null;
        }

        if (!string.Equals(Path.GetFileName(serverProjectDir), ServerProjectDir, StringComparison.Ordinal))
        {
            return null;
        }

        var config = Path.GetFileName(configDir);
        var gameServerRoot = Path.GetDirectoryName(serverProjectDir);
        if (gameServerRoot is null)
        {
            return null;
        }

        return Path.Combine(gameServerRoot, HostProjectDir, "bin", config, tfm, HostDllName);
    }
}
