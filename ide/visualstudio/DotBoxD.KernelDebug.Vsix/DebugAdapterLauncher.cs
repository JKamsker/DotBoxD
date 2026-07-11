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
        if (launchInfo.LaunchType != LaunchType.Attach || launchInfo.AttachProcessId <= 0)
        {
            throw new InvalidOperationException("DotBoxD kernel debugging attaches to a plugin process that already owns a debug bridge.");
        }

        launchInfo.LaunchJson = "{\"request\":\"attach\",\"processId\":" +
            launchInfo.AttachProcessId.ToString(CultureInfo.InvariantCulture) + "}";
        WriteDiagnostic($"configure attach {launchInfo.AttachProcessId}");
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

        WriteDiagnostic("launch adapter");
        var process = targetInterop.ExecuteCommandAsync("dotnet", Quote(adapter));
        WriteDiagnostic("adapter launched");
        return process;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

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
