using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GameServerReadinessTests
{
    [Fact]
    public async Task GameServer_exits_when_plugin_process_never_reaches_readiness()
    {
        var fakePlugin = BuildNonConnectingPlugin();
        using var process = StartGameServer(fakePlugin);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            var exit = process.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(4))) != exit)
            {
                KillProcessTree(process);
                var output = await CapturedOutputAsync(stdout, stderr);
                Assert.Fail("Game server did not exit when the plugin stayed alive without connecting." + output);
            }

            Assert.Equal(1, process.ExitCode);
            Assert.Contains(
                "plugin did not connect",
                await CapturedOutputAsync(stdout, stderr),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            KillProcessTree(process);
        }
    }

    private static Process StartGameServer(string fakePlugin)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(GameServerAssemblyPath());
        startInfo.Environment["SAFEIR_GAME_PLUGIN_DLL"] = fakePlugin;
        startInfo.Environment["DOTBOXD_GAME_PLUGIN_READINESS_TIMEOUT_MS"] = "200";

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start GameServer sample process.");
    }

    private static string BuildNonConnectingPlugin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotboxd-nonconnecting-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, "NonConnectingPlugin.dll");
        var syntaxTree = CSharpSyntaxTree.ParseText(
            """
            using System;
            using System.Threading.Tasks;

            public static class Program
            {
                public static async Task<int> Main(string[] args)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    return 0;
                }
            }
            """);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "NonConnectingPlugin",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        var emit = compilation.Emit(outputPath);
        Assert.True(
            emit.Success,
            "Failed to compile fake plugin: " + string.Join(Environment.NewLine, emit.Diagnostics));
        WriteRuntimeConfig(outputPath);
        return outputPath;
    }

    private static void WriteRuntimeConfig(string assemblyPath)
    {
        var version = Environment.Version;
        File.WriteAllText(
            Path.ChangeExtension(assemblyPath, ".runtimeconfig.json"),
            $$"""
            {
              "runtimeOptions": {
                "tfm": "net{{version.Major}}.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "{{version.Major}}.{{version.Minor}}.{{version.Build}}"
                }
              }
            }
            """);
    }

    private static async Task<string> CapturedOutputAsync(Task<string> stdout, Task<string> stderr)
        => Environment.NewLine +
           "--- stdout ---" + Environment.NewLine +
           await stdout.ConfigureAwait(false) +
           Environment.NewLine +
           "--- stderr ---" + Environment.NewLine +
           await stderr.ConfigureAwait(false);

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string GameServerAssemblyPath()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configuration = output.Parent!.Name;
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "Kernels",
            "GameServer",
            "DotBoxD.Kernels.Game.Server",
            "bin",
            configuration,
            "net10.0",
            "DotBoxD.Kernels.Game.Server.dll"));
    }
}
