namespace SafeIR.Runtime;

using SafeIR;

public static class SafeFileSystem
{
    public static async ValueTask<string> ReadTextAsync(
        SandboxContext context,
        SandboxPath path,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try {
            var resolved = ResolveReadPath(context, path);
            var info = new FileInfo(resolved.FullPath);
            if (!info.Exists) {
                throw Error(SandboxErrorCode.NotFound, "file.readText denied: file was not found");
            }

            var maxBytes = ReadLong(resolved.Grant, "maxBytesPerRun", context.Budget.Limits.MaxFileBytesRead);
            if (info.Length > maxBytes) {
                throw Error(SandboxErrorCode.QuotaExceeded, "file.readText denied: file exceeds read limit");
            }

            context.Budget.ChargeFileRead(info.Length);
            context.ChargeFuel(50 + info.Length);
            var text = await File.ReadAllTextAsync(info.FullName, cancellationToken).ConfigureAwait(false);
            context.ChargeAllocation(text.Length * sizeof(char));
            Audit(context, startedAt, true, resolved.SanitizedPath, info.Length, null);
            return text;
        }
        catch (SandboxRuntimeException ex) {
            Audit(context, startedAt, false, Sanitize(path.RelativePath), null, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "file.readText cancelled");
            Audit(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception) {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "file.readText failed");
            Audit(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    private static ResolvedPath ResolveReadPath(SandboxContext context, SandboxPath path)
    {
        context.RequireCapability("file.read");
        var grant = context.GetCapability("file.read");
        if (!grant.Parameters.TryGetValue("root", out var root) || string.IsNullOrWhiteSpace(root)) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.readText denied: file root is not configured");
        }

        var relative = NormalizeRelative(path.RelativePath);
        var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!IsUnderRoot(rootFull, fullPath)) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.readText denied: path is outside the granted sandbox root");
        }

        EnsureNoReparsePoint(rootFull, fullPath);
        EnsureExtensionAllowed(grant, fullPath);
        return new ResolvedPath(grant, fullPath, "sandbox://file.read/" + relative.Replace('\\', '/'));
    }

    private static string NormalizeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Uri.TryCreate(path, UriKind.Absolute, out _) ||
            Path.IsPathFullyQualified(path) ||
            path.StartsWith('\\') ||
            path.StartsWith('/')) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.readText denied: absolute paths are not allowed");
        }

        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static void EnsureNoReparsePoint(string rootFull, string fullPath)
    {
        CheckAttributes(rootFull);
        var parent = Path.GetDirectoryName(fullPath);
        if (parent is not null && Directory.Exists(parent)) {
            CheckAttributes(parent);
        }

        if (File.Exists(fullPath)) {
            CheckAttributes(fullPath);
        }
    }

    private static void CheckAttributes(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.readText denied: reparse points are not allowed");
        }
    }

    private static void EnsureExtensionAllowed(CapabilityGrant grant, string fullPath)
    {
        if (!grant.Parameters.TryGetValue("allowedExtensions", out var allowed) || string.IsNullOrWhiteSpace(allowed)) {
            return;
        }

        var extension = Path.GetExtension(fullPath);
        var values = allowed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!values.Any(v => StringComparer.OrdinalIgnoreCase.Equals(v, extension))) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.readText denied: extension is not allowed");
        }
    }

    private static void Audit(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            success,
            BindingId: "file.readText",
            CapabilityId: "file.read",
            Effect: SandboxEffect.FileRead,
            ResourceId: resource,
            ErrorCode: error,
            Bytes: bytes));

    private static bool IsUnderRoot(string rootFull, string fullPath)
        => fullPath.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string EnsureTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static long ReadLong(CapabilityGrant grant, string key, long fallback)
        => grant.Parameters.TryGetValue(key, out var value) && long.TryParse(value, out var parsed) ? parsed : fallback;

    private static string Sanitize(string value) => value.Replace('\\', '/');

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));

    private sealed record ResolvedPath(CapabilityGrant Grant, string FullPath, string SanitizedPath);
}
