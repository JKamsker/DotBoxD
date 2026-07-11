using System.Diagnostics;
using System.Globalization;

namespace DotBoxD.KernelDebug.VisualStudio;

internal static class DotBoxDBridgeDiscovery
{
    private const string BridgeDirectoryName = "DotBoxD\\Debug";

    public static int? FindRecentProcessId(string solutionDirectory, DateTime startedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(solutionDirectory))
        {
            return null;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            BridgeDirectoryName);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var earliestDescriptorWrite = startedAtUtc.AddSeconds(-2);
        foreach (var descriptorPath in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (File.GetLastWriteTimeUtc(descriptorPath) < earliestDescriptorWrite ||
                !TryGetProcessId(descriptorPath, out var processId) ||
                !IsProcessFromSolution(processId, solutionDirectory))
            {
                continue;
            }

            return processId;
        }

        return null;
    }

    private static bool TryGetProcessId(string descriptorPath, out int processId)
        => int.TryParse(
            Path.GetFileNameWithoutExtension(descriptorPath),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out processId) && processId > 0;

    private static bool IsProcessFromSolution(int processId, string solutionDirectory)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var executablePath = process.MainModule?.FileName;
            return executablePath is not null && IsNestedUnder(executablePath, solutionDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsNestedUnder(string path, string directory)
    {
        var normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
