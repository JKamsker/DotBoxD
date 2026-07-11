using System.Buffers;
using System.Text;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

public static class SafeFileSystem
{
    public static async ValueTask<string> ReadTextAsync(
        SandboxContext context,
        SandboxPath path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(path);

        if (context.CancellationToken.IsCancellationRequested)
        {
            throw Error(SandboxErrorCode.Cancelled, "file.readText cancelled");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var fileBytesReadBefore = context.Budget.FileBytesRead;
        try
        {
            var resolved = SafeFilePathResolver.Resolve(context, path, "file.read", "file.readText");
            var info = new FileInfo(resolved.FullPath);
            if (!info.Exists)
            {
                throw Error(SandboxErrorCode.NotFound, "file.readText denied: file was not found");
            }

            var maxBytes = SafeFileGrantReader.Read(resolved.Grant).MaxBytesPerRun;
            if (info.Length > maxBytes)
            {
                throw Error(SandboxErrorCode.QuotaExceeded, "file.readText denied: file exceeds read limit");
            }

            using var bytes = await ReadLimitedBytesAsync(context, resolved, maxBytes, cancellationToken).ConfigureAwait(false);
            var length = CheckedLength(bytes.Length);
            var buffer = bytes.GetBuffer();
            context.ChargeFuel(length);
            context.ChargeStringAllocation(Encoding.UTF8.GetCharCount(buffer, 0, length));
            var text = Encoding.UTF8.GetString(buffer, 0, length);
            context.RecordStringReturnCredit(text);
            SafeFileAudit.Read(context, startedAt, true, resolved.SanitizedPath, length, null);
            return text;
        }
        catch (SandboxRuntimeException ex)
        {
            SafeFileAudit.Read(context, startedAt, false, SafeFilePathResolver.FailureResource(path, "file.read"), ObservedReadBytes(context, fileBytesReadBefore), ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested &&
                                                !cancellationToken.IsCancellationRequested)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, "file.readText denied: request timed out");
            SafeFileAudit.Read(context, startedAt, false, SafeFilePathResolver.FailureResource(path, "file.read"), ObservedReadBytes(context, fileBytesReadBefore), error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "file.readText cancelled");
            SafeFileAudit.Read(context, startedAt, false, SafeFilePathResolver.FailureResource(path, "file.read"), ObservedReadBytes(context, fileBytesReadBefore), error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "file.readText failed");
            SafeFileAudit.Read(context, startedAt, false, SafeFilePathResolver.FailureResource(path, "file.read"), ObservedReadBytes(context, fileBytesReadBefore), error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    public static async ValueTask WriteTextAsync(
        SandboxContext context,
        SandboxPath path,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        if (context.CancellationToken.IsCancellationRequested)
        {
            throw Error(SandboxErrorCode.Cancelled, "file.writeText cancelled");
        }

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = SafeFilePathResolver.Resolve(context, path, "file.write", "file.writeText");
            var byteCount = Encoding.UTF8.GetByteCount(text);
            var permission = SafeFileWritePublisher.EnsureAllowed(resolved.Grant, resolved.FullPath, byteCount);
            context.Budget.ChargeFileWrite(byteCount);
            context.ChargeAllocation(byteCount);
            context.ChargeFuel(byteCount);
            var bytes = Encoding.UTF8.GetBytes(text);

            context.Budget.CheckDeadline();
            EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
            FileSystem.SafeFileSystem.EnsureDirectWritePath(resolved.RootFull, resolved.FullPath);
            SafeFileWritePublisher.EnsureParentDirectory(resolved.RootFull, resolved.FullPath, permission);
            var tempPath = resolved.FullPath + ".tmp-" + FileSystem.SafeFileSystem.CreateTempSuffix();
            try
            {
                FileSystem.SafeFileSystem.BeforeTempCreateForTests.Value?.Invoke();
                EnsureNoReparsePoint(resolved.RootFull, tempPath);
                var temp = SafeFileNoFollow.CreateNewWrite(tempPath);
                await using (temp.ConfigureAwait(false))
                {
                    await temp.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await temp.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                context.CancellationToken.ThrowIfCancellationRequested();
                context.Budget.CheckDeadline();
                EnsureNoReparsePoint(resolved.RootFull, tempPath);
                EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
                SafeFileWritePublisher.PublishTempFile(tempPath, resolved.FullPath, permission);
                context.Budget.CheckDeadline();
            }
            finally
            {
                SafeFileWritePublisher.TryDelete(tempPath);
            }

            SafeFileAudit.Write(
                context,
                startedAt,
                true,
                resolved.SanitizedPath,
                byteCount,
                permission.TargetExisted,
                null);
        }
        catch (SandboxRuntimeException ex)
        {
            SafeFileAudit.Write(
                context,
                startedAt,
                false,
                SafeFilePathResolver.FailureResource(path, "file.write"),
                null,
                false,
                ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested &&
                                                !cancellationToken.IsCancellationRequested)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, "file.writeText denied: request timed out");
            SafeFileAudit.Write(
                context,
                startedAt,
                false,
                SafeFilePathResolver.FailureResource(path, "file.write"),
                null,
                false,
                error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "file.writeText cancelled");
            SafeFileAudit.Write(
                context,
                startedAt,
                false,
                SafeFilePathResolver.FailureResource(path, "file.write"),
                null,
                false,
                error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "file.writeText failed");
            SafeFileAudit.Write(
                context,
                startedAt,
                false,
                SafeFilePathResolver.FailureResource(path, "file.write"),
                null,
                false,
                error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    private static long? ObservedReadBytes(SandboxContext context, long fileBytesReadBefore)
    {
        var observedBytes = context.Budget.FileBytesRead - fileBytesReadBefore;
        return observedBytes > 0 ? observedBytes : null;
    }

    private static async ValueTask<MemoryStream> ReadLimitedBytesAsync(
        SandboxContext context,
        SafeFileResolvedPath resolved,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var stream = SafeFileNoFollow.OpenRead(resolved.FullPath);
        await using (stream.ConfigureAwait(false))
        {
            EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
            var memory = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                while (true)
                {
                    context.Budget.CheckDeadline();
                    var read = await SafeFileNoFollow.ReadAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
                    context.Budget.CheckDeadline();
                    if (read == 0)
                    {
                        return memory;
                    }

                    context.Budget.ChargeFileRead(read);
                    if (memory.Length + read > maxBytes)
                    {
                        throw Error(SandboxErrorCode.QuotaExceeded, "file.readText denied: file exceeds read limit");
                    }

                    context.ChargeAllocation(read);
                    memory.Write(buffer, 0, read);
                }
            }
            catch
            {
                memory.Dispose();
                throw;
            }
            finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
        }
    }

    private static int CheckedLength(long length)
    {
        if (length > int.MaxValue)
        {
            throw Error(SandboxErrorCode.QuotaExceeded, "file.readText denied: file exceeds read limit");
        }

        return (int)length;
    }

    internal static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));

    internal static void EnsureNoReparsePoint(string rootFull, string fullPath) =>
        SafeFilePathGuard.EnsureNoReparsePoint(rootFull, fullPath);

    internal static bool IsRootEscapeRelativePath(string relative) =>
        SafeFilePathGuard.IsRootEscapeRelativePath(relative);
}
