using System.Globalization;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace DotBoxD.KernelDebug.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuid)]
public sealed class DotBoxDKernelAutoAttachPackage : AsyncPackage
{
    public const string PackageGuid = "9201F680-276F-4F2E-898E-F94EA2E3F3DF";
    private const string ActivityLogSource = "DotBoxD Kernel Debugger";

    private static readonly Guid KernelDebugEngineId = new("82F49048-CECF-432B-B6D6-F78030C89496");
    private readonly HashSet<int> _autoAttachedProcessIds = [];
    private DTE? _dte;
    private DebuggerEvents? _debuggerEvents;
    private IVsDebugger3? _debugger;

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        _dte = (DTE?)await GetServiceAsync(typeof(SDTE))
            ?? throw new InvalidOperationException("The Visual Studio automation service is unavailable.");
        var debuggerService = await GetServiceAsync(typeof(SVsShellDebugger));
        _debugger = debuggerService as IVsDebugger3
            ?? throw new InvalidOperationException("The Visual Studio debugger service is unavailable.");
        _debuggerEvents = _dte.Events.DebuggerEvents;
        _debuggerEvents.OnEnterRunMode += OnEnterRunMode;
    }

    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposing && _debuggerEvents is not null)
        {
            _debuggerEvents.OnEnterRunMode -= OnEnterRunMode;
        }

        base.Dispose(disposing);
    }

    private void OnEnterRunMode(dbgEventReason reason)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var solutionPath = _dte?.Solution.IsOpen == true ? _dte.Solution.FullName : null;
        var solutionDirectory = string.IsNullOrWhiteSpace(solutionPath)
            ? null
            : Path.GetDirectoryName(solutionPath);
        if (solutionDirectory is not null)
        {
            _ = JoinableTaskFactory.RunAsync(
                () => AttachToNewBridgeAsync(solutionDirectory, DateTime.UtcNow));
        }
    }

    private async Task AttachToNewBridgeAsync(string solutionDirectory, DateTime startedAtUtc)
    {
        try
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                var processId = await Task.Run(
                        () => DotBoxDBridgeDiscovery.FindRecentProcessId(solutionDirectory, startedAtUtc))
                    .ConfigureAwait(false);
                if (processId is { } candidateProcessId)
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (_dte?.Debugger.CurrentMode != dbgDebugMode.dbgRunMode)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
                        continue;
                    }

                    if (_autoAttachedProcessIds.Add(candidateProcessId))
                    {
                        LaunchKernelDebugger(candidateProcessId);
                        ActivityLog.LogInformation(
                            ActivityLogSource,
                            $"Automatically enabled kernel debugging for process {candidateProcessId}.");
                    }

                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ActivityLog.LogError(ActivityLogSource, exception.ToString());
        }
    }

    private void LaunchKernelDebugger(int processId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_debugger is null || _dte is null)
        {
            return;
        }

        using var process = System.Diagnostics.Process.GetProcessById(processId);
        if (process.HasExited)
        {
            throw new InvalidOperationException($"Plugin process {processId} exited before kernel debugging started.");
        }

        var extensionDirectory = Path.GetDirectoryName(typeof(DotBoxDKernelAutoAttachPackage).Assembly.Location)
            ?? throw new InvalidOperationException("The VSIX installation directory is unavailable.");
        var companionExecutable = Path.Combine(extensionDirectory, "adapter", "DotBoxD.DebugAdapter.exe");
        if (!File.Exists(companionExecutable))
        {
            throw new FileNotFoundException("The packaged DotBoxD debug adapter is missing.", companionExecutable);
        }

        var target = new VsDebugTargetInfo3
        {
            dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
            bstrExe = companionExecutable,
            bstrCurDir = Path.GetDirectoryName(companionExecutable),
            bstrOptions = "{\"request\":\"attach\",\"processId\":" +
                processId.ToString(CultureInfo.InvariantCulture) + "}",
            guidLaunchDebugEngine = KernelDebugEngineId,
            fSendToOutputWindow = true
        };
        ErrorHandler.ThrowOnFailure(_debugger.LaunchDebugTargets3(
            1,
            [target],
            [new VsDebugTargetProcessInfo()]));
    }

}
