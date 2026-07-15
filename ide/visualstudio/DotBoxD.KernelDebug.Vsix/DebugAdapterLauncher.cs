using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Debugger.DebugAdapterHost.Interfaces;

namespace DotBoxD.KernelDebug.VisualStudio;

[ComVisible(true)]
[Guid(LauncherClassId)]
public sealed class DebugAdapterLauncher : IAdapterLauncher
{
    public const string LauncherClassId = "80223DBF-71D6-4568-BF29-51F9613ACE15";

    public void Initialize(IDebugAdapterHostContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        WriteDiagnostic("initialize");
    }

    public void UpdateLaunchOptions(IAdapterLaunchInfo launchInfo)
    {
        if (launchInfo is null)
        {
            throw new ArgumentNullException(nameof(launchInfo));
        }
        if (launchInfo.LaunchType == LaunchType.Attach && launchInfo.AttachProcessId > 0)
        {
            launchInfo.LaunchJson = "{\"request\":\"attach\",\"processId\":" +
                launchInfo.AttachProcessId.ToString(CultureInfo.InvariantCulture) + "}";
            WriteDiagnostic($"configure attach {launchInfo.AttachProcessId}");
            return;
        }

        if (launchInfo.LaunchType != LaunchType.Launch || string.IsNullOrWhiteSpace(launchInfo.LaunchJson))
        {
            throw new InvalidOperationException(
                "DotBoxD kernel debugging requires an attach process id or companion-target launch options.");
        }

        WriteDiagnostic("configure companion target");
    }

    public ITargetHostProcess LaunchAdapter(IAdapterLaunchInfo launchInfo, ITargetHostInterop targetInterop)
    {
        if (launchInfo is null)
        {
            throw new ArgumentNullException(nameof(launchInfo));
        }

        if (targetInterop is null)
        {
            throw new ArgumentNullException(nameof(targetInterop));
        }
        var directory = Path.GetDirectoryName(typeof(DebugAdapterLauncher).Assembly.Location)
            ?? throw new InvalidOperationException("The VSIX installation directory is unavailable.");
        var adapter = Path.Combine(directory, "adapter", "DotBoxD.DebugAdapter.dll");
        if (!File.Exists(adapter))
        {
            throw new FileNotFoundException("The packaged DotBoxD debug adapter is missing.", adapter);
        }
        var dotnetHost = ResolveDotNetHost();

        WriteDiagnostic("launch adapter");
        var diagnosticPath = Environment.GetEnvironmentVariable("DOTBOXD_VSIX_DIAGNOSTIC_LOG");
        var arguments = Quote(adapter);
        if (!string.IsNullOrWhiteSpace(diagnosticPath))
        {
            arguments += " --diagnostic-log " + Quote(diagnosticPath);
        }

        var process = targetInterop.ExecuteCommandAsync(dotnetHost, arguments);
        WriteDiagnostic("adapter launched");
        return process;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string ResolveDotNetHost()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var host = string.IsNullOrWhiteSpace(dotnetRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")
            : Path.Combine(dotnetRoot, "dotnet.exe");
        return File.Exists(host)
            ? host
            : throw new FileNotFoundException("The .NET host required by DotBoxD.DebugAdapter was not found.", host);
    }

    private static void WriteDiagnostic(string message)
    {
        var path = Environment.GetEnvironmentVariable("DOTBOXD_VSIX_DIAGNOSTIC_LOG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Diagnostic logging must never affect debugger startup.
        }
    }
}
