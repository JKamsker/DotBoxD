using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

internal static class SafeFilePathResolver
{
    public static SafeFileResolvedPath Resolve(
        SandboxContext context,
        SandboxPath path,
        string capabilityId,
        string bindingId)
    {
        // GetCapability performs authorization and denial audit in one indexed lookup.
        var grant = context.GetCapability(capabilityId);
        if (!grant.Parameters.TryGetValue("root", out var root) || string.IsNullOrWhiteSpace(root))
        {
            throw SafeFileSystem.Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: file root is not configured");
        }

        var relative = NormalizeRelative(path.RelativePath);
        var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!IsUnderRoot(rootFull, fullPath))
        {
            throw SafeFileSystem.Error(
                SandboxErrorCode.PermissionDenied,
                $"{bindingId} denied: path is outside the granted sandbox root");
        }

        SafeFilePathGuard.EnsureNoReparsePoint(rootFull, fullPath);
        EnsureExtensionAllowed(grant, fullPath, bindingId);
        return new SafeFileResolvedPath(
            grant,
            rootFull,
            fullPath,
            $"sandbox://{capabilityId}/" + relative.Replace('\\', '/'));
    }

    public static string FailureResource(SandboxPath path, string capabilityId)
        => SandboxLiteralConstraints.IsPortableRelativePath(path.RelativePath)
            ? path.RelativePath
            : $"sandbox://{capabilityId}/[invalid-path]";

    private static string NormalizeRelative(string path)
    {
        if (!SandboxLiteralConstraints.IsPortableRelativePath(path))
        {
            throw SafeFileSystem.Error(
                SandboxErrorCode.PermissionDenied,
                "file path denied: path is not a portable relative path");
        }

        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
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
            throw SafeFileSystem.Error(
                SandboxErrorCode.PermissionDenied,
                $"{bindingId} denied: extension is not allowed");
        }
    }

    private static bool IsUnderRoot(string rootFull, string fullPath)
        => fullPath.StartsWith(
            rootFull,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string EnsureTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}

internal sealed record SafeFileResolvedPath(
    CapabilityGrant Grant,
    string RootFull,
    string FullPath,
    string SanitizedPath);
