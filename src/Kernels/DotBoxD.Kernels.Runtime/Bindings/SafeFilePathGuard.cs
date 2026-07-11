using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

internal static class SafeFilePathGuard
{
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static void EnsureNoReparsePoint(string rootFull, string fullPath)
    {
        var root = Path.TrimEndingDirectorySeparator(rootFull);
        CheckAttributes(root);

        var relative = Path.GetRelativePath(root, fullPath);
        if (IsRootEscapeRelativePath(relative))
        {
            throw SafeFileSystem.Error(
                SandboxErrorCode.PermissionDenied,
                "file access denied: path is outside the granted sandbox root");
        }

        var current = root;
        foreach (var part in relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (SafeFilePathAttributes.IsReparsePointOrDeny(current))
            {
                throw SafeFileSystem.Error(
                    SandboxErrorCode.PermissionDenied,
                    "file access denied: reparse points are not allowed");
            }
        }
    }

    public static bool IsRootEscapeRelativePath(string relative)
        => Path.IsPathFullyQualified(relative) ||
           relative.Equals("..", StringComparison.Ordinal) ||
           relative.StartsWith("../", StringComparison.Ordinal) ||
           relative.StartsWith(@"..\", StringComparison.Ordinal);

    private static void CheckAttributes(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw SafeFileSystem.Error(
                SandboxErrorCode.PermissionDenied,
                "file access denied: reparse points are not allowed");
        }
    }
}
