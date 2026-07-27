using System.Runtime.InteropServices;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

internal static class AspNetCoreTestReferences
{
    public static string FindAssembly(string fileName)
    {
        var path = Path.Combine(HighestVersionedReferenceDirectory(), fileName);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                $"Could not find ASP.NET Core runtime assembly '{fileName}'.",
                fileName);
    }

    public static IEnumerable<string> AssemblyPaths(string searchPattern)
        => Directory.EnumerateFiles(HighestVersionedReferenceDirectory(), searchPattern);

    private static string HighestVersionedReferenceDirectory()
    {
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dotnetRoot = Directory.GetParent(runtimeDirectory)?.Parent?.Parent?.FullName ??
            throw new DirectoryNotFoundException("Could not locate the dotnet installation directory.");
        var aspNetCoreRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.AspNetCore.App.Ref");
        var targetFramework = $"net{Environment.Version.Major}.0";
        var selected = Directory
            .EnumerateDirectories(aspNetCoreRoot)
            .Select(path => (Path: path, Version: VersionPrefix(path)))
            .Where(candidate => candidate.Version is not null)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => Path.Combine(candidate.Path, "ref", targetFramework))
            .FirstOrDefault(Directory.Exists);

        return selected ?? throw new DirectoryNotFoundException(
            $"Could not locate a versioned Microsoft.AspNetCore.App reference pack for {targetFramework}.");
    }

    private static Version? VersionPrefix(string path)
    {
        var directoryName = Path.GetFileName(path);
        var separator = directoryName.IndexOf('-', StringComparison.Ordinal);
        var versionText = separator < 0 ? directoryName : directoryName[..separator];
        return Version.TryParse(versionText, out var version) ? version : null;
    }
}
