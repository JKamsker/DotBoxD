using System.Runtime.InteropServices;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

internal static class AspNetCoreTestReferences
{
    public static string FindAssembly(string fileName)
    {
        var path = Path.Combine(HighestVersionedRuntimeDirectory(), fileName);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                $"Could not find ASP.NET Core runtime assembly '{fileName}'.",
                fileName);
    }

    public static IEnumerable<string> AssemblyPaths(string searchPattern)
        => Directory.EnumerateFiles(HighestVersionedRuntimeDirectory(), searchPattern);

    private static string HighestVersionedRuntimeDirectory()
    {
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sharedRoot = Directory.GetParent(runtimeDirectory)?.Parent?.FullName ??
            throw new DirectoryNotFoundException("Could not locate the dotnet shared-runtime directory.");
        var aspNetCoreRoot = Path.Combine(sharedRoot, "Microsoft.AspNetCore.App");
        var selected = Directory
            .EnumerateDirectories(aspNetCoreRoot)
            .Select(path => (Path: path, Version: VersionPrefix(path)))
            .Where(candidate => candidate.Version is not null)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();

        return selected ?? throw new DirectoryNotFoundException(
            "Could not locate a versioned Microsoft.AspNetCore.App shared runtime.");
    }

    private static Version? VersionPrefix(string path)
    {
        var directoryName = Path.GetFileName(path);
        var separator = directoryName.IndexOf('-', StringComparison.Ordinal);
        var versionText = separator < 0 ? directoryName : directoryName[..separator];
        return Version.TryParse(versionText, out var version) ? version : null;
    }
}
