namespace SafeIR.Runtime;

using System.Globalization;
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
            var resolved = ResolvePath(context, path, "file.read", "file.readText");
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
            context.ChargeString(text);
            AuditRead(context, startedAt, true, resolved.SanitizedPath, info.Length, null);
            return text;
        }
        catch (SandboxRuntimeException ex) {
            AuditRead(context, startedAt, false, Sanitize(path.RelativePath), null, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "file.readText cancelled");
            AuditRead(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception) {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "file.readText failed");
            AuditRead(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    public static async ValueTask WriteTextAsync(
        SandboxContext context,
        SandboxPath path,
        string text,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try {
            var resolved = ResolvePath(context, path, "file.write", "file.writeText");
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            EnsureWriteAllowed(resolved, bytes.Length);
            context.Budget.ChargeFileWrite(bytes.Length);
            var directory = Path.GetDirectoryName(resolved.FullPath);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory);
            }

            EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
            var tempPath = resolved.FullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
            File.Move(tempPath, resolved.FullPath, overwrite: true);
            context.ChargeFuel(50 + bytes.Length);
            AuditWrite(context, startedAt, true, resolved.SanitizedPath, bytes.Length, null);
        }
        catch (SandboxRuntimeException ex) {
            AuditWrite(context, startedAt, false, Sanitize(path.RelativePath), null, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "file.writeText cancelled");
            AuditWrite(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception) {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "file.writeText failed");
            AuditWrite(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    private static ResolvedPath ResolvePath(SandboxContext context, SandboxPath path, string capabilityId, string bindingId)
    {
        context.RequireCapability(capabilityId);
        var grant = context.GetCapability(capabilityId);
        if (!grant.Parameters.TryGetValue("root", out var root) || string.IsNullOrWhiteSpace(root)) {
            throw Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: file root is not configured");
        }

        var relative = NormalizeRelative(path.RelativePath);
        var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!IsUnderRoot(rootFull, fullPath)) {
            throw Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: path is outside the granted sandbox root");
        }

        EnsureNoReparsePoint(rootFull, fullPath);
        EnsureExtensionAllowed(grant, fullPath);
        return new ResolvedPath(grant, rootFull, fullPath, $"sandbox://{capabilityId}/" + relative.Replace('\\', '/'));
    }

    private static string NormalizeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Uri.TryCreate(path, UriKind.Absolute, out _) ||
            Path.IsPathFullyQualified(path) ||
            path.StartsWith('\\') ||
            path.StartsWith('/')) {
            throw Error(SandboxErrorCode.PermissionDenied, "file path denied: absolute paths are not allowed");
        }

        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static void EnsureWriteAllowed(ResolvedPath resolved, long byteCount)
    {
        var exists = File.Exists(resolved.FullPath);
        if (exists && !ReadBool(resolved.Grant, "allowOverwrite", fallback: true)) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: overwrite is not allowed");
        }

        if (!exists && !ReadBool(resolved.Grant, "allowCreate", fallback: true)) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: create is not allowed");
        }

        var maxBytes = ReadLong(resolved.Grant, "maxBytesPerRun", long.MaxValue);
        if (byteCount > maxBytes) {
            throw Error(SandboxErrorCode.QuotaExceeded, "file.writeText denied: content exceeds write limit");
        }
    }

    private static void EnsureNoReparsePoint(string rootFull, string fullPath)
    {
        var root = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        CheckAttributes(root);

        var relative = Path.GetRelativePath(root, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative)) {
            throw Error(SandboxErrorCode.PermissionDenied, "file access denied: path is outside the granted sandbox root");
        }

        var current = root;
        foreach (var part in relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, part);
            if (Directory.Exists(current) || File.Exists(current)) {
                CheckAttributes(current);
            }
        }
    }

    private static void CheckAttributes(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) {
            throw Error(SandboxErrorCode.PermissionDenied, "file access denied: reparse points are not allowed");
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

    private static void AuditRead(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => Audit(context, startedAt, success, "file.readText", "file.read", SandboxEffect.FileRead, resource, bytes, error);

    private static void AuditWrite(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => Audit(context, startedAt, success, "file.writeText", "file.write", SandboxEffect.FileWrite, resource, bytes, error);

    private static void Audit(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string bindingId,
        string capabilityId,
        SandboxEffect effect,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            success,
            BindingId: bindingId,
            CapabilityId: capabilityId,
            Effect: effect,
            ResourceId: resource,
            ErrorCode: error,
            Bytes: bytes));

    private static bool IsUnderRoot(string rootFull, string fullPath)
        => fullPath.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string EnsureTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static long ReadLong(CapabilityGrant grant, string key, long fallback)
    {
        if (!grant.Parameters.TryGetValue(key, out var value)) {
            return fallback;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0) {
            throw Error(SandboxErrorCode.PermissionDenied, $"file grant denied: parameter '{key}' is invalid");
        }

        return parsed;
    }

    private static bool ReadBool(CapabilityGrant grant, string key, bool fallback)
    {
        if (!grant.Parameters.TryGetValue(key, out var value)) {
            return fallback;
        }

        if (!bool.TryParse(value, out var parsed)) {
            throw Error(SandboxErrorCode.PermissionDenied, $"file grant denied: parameter '{key}' is invalid");
        }

        return parsed;
    }

    private static string Sanitize(string value) => value.Replace('\\', '/');

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));

    private sealed record ResolvedPath(CapabilityGrant Grant, string RootFull, string FullPath, string SanitizedPath);
}
