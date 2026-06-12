namespace SafeIR.Compiler;

using SafeIR;

internal static class PersistentCompiledArtifactCacheRootGuard
{
    public static void Validate(string rootDirectory)
    {
        var info = new DirectoryInfo(rootDirectory);
        if (!info.Exists)
        {
            throw Denied("compiled cache directory does not exist");
        }

        info.Refresh();
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Denied("compiled cache directory must not be a reparse point");
        }

        ValidateUnixMode(info);
        ProbeExclusiveWrite(info.FullName);
    }

    private static void ValidateUnixMode(DirectoryInfo info)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(info.FullName);
        const UnixFileMode unsafeWrite =
            UnixFileMode.GroupWrite |
            UnixFileMode.OtherWrite;
        if ((mode & unsafeWrite) != 0)
        {
            throw Denied("compiled cache directory must not be group- or world-writable");
        }
    }

    private static void ProbeExclusiveWrite(string rootDirectory)
    {
        var path = Path.Combine(rootDirectory, ".safe-ir-cache-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
            {
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static SandboxRuntimeException Denied(string message)
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, message));
}
