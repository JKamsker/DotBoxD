using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

public static partial class SafeFileSystem
{
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private static ResolvedPath ResolvePath(SandboxContext context, SandboxPath path, string capabilityId, string bindingId)
    {
        // GetCapability performs the authorization (and denial audit) in one indexed
        // lookup, so a preceding RequireCapability would only repeat the same scan.
        var grant = context.GetCapability(capabilityId);
        if (!grant.Parameters.TryGetValue("root", out var root) || string.IsNullOrWhiteSpace(root))
        {
            throw Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: file root is not configured");
        }

        var relative = NormalizeRelative(path.RelativePath);
        var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!IsUnderRoot(rootFull, fullPath))
        {
            throw Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: path is outside the granted sandbox root");
        }

        EnsureNoReparsePoint(rootFull, fullPath);
        EnsureExtensionAllowed(grant, fullPath, bindingId);
        return new ResolvedPath(grant, rootFull, fullPath, $"sandbox://{capabilityId}/" + relative.Replace('\\', '/'));
    }

    private static string NormalizeRelative(string path)
    {
        if (!SandboxLiteralConstraints.IsPortableRelativePath(path))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "file path denied: path is not a portable relative path");
        }

        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string FailureResource(SandboxPath path, string capabilityId)
        => SandboxLiteralConstraints.IsPortableRelativePath(path.RelativePath)
            ? path.RelativePath
            : $"sandbox://{capabilityId}/[invalid-path]";

    internal static void EnsureNoReparsePoint(string rootFull, string fullPath)
    {
        var root = Path.TrimEndingDirectorySeparator(rootFull);
        CheckAttributes(root);

        var relative = Path.GetRelativePath(root, fullPath);
        if (IsRootEscapeRelativePath(relative))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "file access denied: path is outside the granted sandbox root");
        }

        var current = root;
        foreach (var part in relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (SafeFilePathAttributes.IsReparsePointOrDeny(current))
            {
                throw Error(SandboxErrorCode.PermissionDenied, "file access denied: reparse points are not allowed");
            }
        }
    }

    internal static bool IsRootEscapeRelativePath(string relative)
        => Path.IsPathFullyQualified(relative) ||
           relative.Equals("..", StringComparison.Ordinal) ||
           relative.StartsWith("../", StringComparison.Ordinal) ||
           relative.StartsWith(@"..\", StringComparison.Ordinal);

    private static void CheckAttributes(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Error(SandboxErrorCode.PermissionDenied, "file access denied: reparse points are not allowed");
        }
    }

    private static void EnsureExtensionAllowed(CapabilityGrant grant, string fullPath, string bindingId)
    {
        var allowed = SafeFileGrantReader.Read(grant).AllowedExtensions;
        if (allowed is null)
        {
            return;
        }

        var extension = Path.GetExtension(fullPath);
        if (!allowed.Contains(extension))
        {
            throw Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: extension is not allowed");
        }
    }

    private static bool IsUnderRoot(string rootFull, string fullPath)
        => fullPath.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string EnsureTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private sealed record ResolvedPath(CapabilityGrant Grant, string RootFull, string FullPath, string SanitizedPath);
}
