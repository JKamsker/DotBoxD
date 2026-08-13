using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Debugging.Clr;

/// <summary>Host limits and serialized context for the disposable trusted C# evaluator worker.</summary>
public sealed record TrustedWorkerDebugEvaluatorOptions
{
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(5);

    public long MemoryLimitBytes { get; init; } = 512L * 1024 * 1024;

    public IReadOnlyCollection<string> ReferencePaths { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> Imports { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, SandboxValue> Context { get; init; } =
        new Dictionary<string, SandboxValue>(StringComparer.Ordinal);
}

internal sealed class TrustedWorkerPluginDebugEvaluator : IPluginDebugEvaluatorProvider
{
    private readonly TrustedWorkerDebugEvaluatorOptions _options;

    public TrustedWorkerPluginDebugEvaluator(TrustedWorkerDebugEvaluatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TimeLimit <= TimeSpan.Zero || options.TimeLimit > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Worker time limit must be between zero and five minutes.");
        }

        if (options.MemoryLimitBytes < 64L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Worker memory limit must be at least 64 MiB.");
        }

        ArgumentNullException.ThrowIfNull(options.ReferencePaths);
        ArgumentNullException.ThrowIfNull(options.Imports);
        ArgumentNullException.ThrowIfNull(options.Context);
        _options = options with
        {
            ReferencePaths = options.ReferencePaths.Select(Path.GetFullPath).ToArray(),
            Imports = options.Imports.ToArray(),
            Context = new Dictionary<string, SandboxValue>(options.Context, StringComparer.Ordinal)
        };
    }

    public string Id => "trusted-worker-roslyn-v1";

    public PluginDebugEvaluationTrustProfile TrustProfile => PluginDebugEvaluationTrustProfile.TrustedWorker;

    public bool SupportsAwait => true;

    public async ValueTask<PluginDebugEvaluationResult> EvaluateAsync(
        PluginDebugEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workerRequest = new ClrDebugWorkerRequest
        {
            Expression = request.Expression,
            AllowAwait = request.AllowAwait,
            Arguments = ClrDebugEvaluationEngine.Snapshot(request.Frame.Arguments),
            Locals = ClrDebugEvaluationEngine.Snapshot(request.Frame.Locals),
            Context = _options.Context.ToDictionary(
                item => item.Key,
                item => ClrDebugValue.FromSandbox(item.Value),
                StringComparer.Ordinal),
            ReferencePaths = _options.ReferencePaths.ToArray(),
            Imports = _options.Imports.ToArray(),
            Assemblies = request.Assemblies.ToDictionary(
                item => item.Key,
                item => item.Value.ToArray(),
                StringComparer.Ordinal)
        };

        try
        {
            var response = await ClrDebugWorkerClient.RunAsync(
                    workerRequest,
                    _options.TimeLimit,
                    _options.MemoryLimitBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            return response.Value is not null
                ? PluginDebugEvaluationResult.Success(response.Value.ToSandbox())
                : Failure(response.Error ?? "The trusted evaluator worker failed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(exception.Message);
        }
    }

    private static PluginDebugEvaluationResult Failure(string message) =>
        PluginDebugEvaluationResult.Failure(new SandboxError(SandboxErrorCode.InvalidInput, message));
}

internal sealed record ClrDebugWorkerRequest
{
    public required string Expression { get; init; }

    public bool AllowAwait { get; init; }

    public required IReadOnlyDictionary<string, ClrDebugValue> Arguments { get; init; }

    public required IReadOnlyDictionary<string, ClrDebugValue> Locals { get; init; }

    public required IReadOnlyDictionary<string, ClrDebugValue> Context { get; init; }

    public required string[] ReferencePaths { get; init; }

    public required string[] Imports { get; init; }

    public required IReadOnlyDictionary<string, byte[]> Assemblies { get; init; }
}

internal sealed record ClrDebugWorkerResponse(ClrDebugValue? Value, string? Error);

internal static class ClrDebugWorkerClient
{
    public static async Task<ClrDebugWorkerResponse> RunAsync(
        ClrDebugWorkerRequest request,
        TimeSpan timeLimit,
        long memoryLimitBytes,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(CreateStartInfo())
            ?? throw new InvalidOperationException("The trusted evaluator worker could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        process.StandardInput.Close();

        using var timeout = new CancellationTokenSource(timeLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            while (!process.HasExited)
            {
                process.Refresh();
                if (process.WorkingSet64 > memoryLimitBytes)
                {
                    process.Kill(entireProcessTree: true);
                    return new ClrDebugWorkerResponse(null, "The trusted evaluator worker exceeded its memory limit.");
                }

                await Task.Delay(25, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return new ClrDebugWorkerResponse(null, "The trusted evaluator worker exceeded its time limit.");
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            return new ClrDebugWorkerResponse(null, string.IsNullOrWhiteSpace(error) ? "Worker exited unexpectedly." : error.Trim());
        }

        return JsonSerializer.Deserialize<ClrDebugWorkerResponse>(output)
            ?? new ClrDebugWorkerResponse(null, "Worker returned an empty response.");
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var workerAssembly = typeof(ClrDebugWorkerClient).Assembly.Location;
        var runtimeConfig = Path.ChangeExtension(workerAssembly, ".runtimeconfig.json");
        var depsFile = Path.ChangeExtension(workerAssembly, ".deps.json");
        if (!File.Exists(runtimeConfig) || !File.Exists(depsFile))
        {
            throw new InvalidOperationException(
                "The trusted evaluator worker runtime files were not deployed with the CLR debugging package.");
        }
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--runtimeconfig");
        startInfo.ArgumentList.Add(runtimeConfig);
        startInfo.ArgumentList.Add("--depsfile");
        startInfo.ArgumentList.Add(depsFile);
        startInfo.ArgumentList.Add(workerAssembly);
        startInfo.ArgumentList.Add("--dotboxd-debug-worker");
        return startInfo;
    }
}

internal static class ClrDebugWorkerProgram
{
    public static async Task<int> RunAsync()
    {
        try
        {
            using var input = new StreamReader(System.Console.OpenStandardInput());
            var request = JsonSerializer.Deserialize<ClrDebugWorkerRequest>(await input.ReadToEndAsync().ConfigureAwait(false))
                ?? throw new InvalidOperationException("Worker request was empty.");
            var context = request.Context.ToDictionary(item => item.Key, item => item.Value.ToClr(), StringComparer.Ordinal);
            var references = request.ReferencePaths.Select(Assembly.LoadFrom).ToArray();
            var value = await ClrDebugEvaluationEngine.EvaluateAsync(
                    request.Expression,
                    request.AllowAwait,
                    request.Arguments,
                    request.Locals,
                    context,
                    references,
                    request.Imports,
                    request.Assemblies.Values.Select(image => (ReadOnlyMemory<byte>)image).ToArray(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            await WriteResponseAsync(new ClrDebugWorkerResponse(value, null)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            await WriteResponseAsync(new ClrDebugWorkerResponse(null, exception.Message)).ConfigureAwait(false);
            return 0;
        }
    }

    private static async Task WriteResponseAsync(ClrDebugWorkerResponse response)
    {
        using var output = new StreamWriter(System.Console.OpenStandardOutput());
        await output.WriteAsync(JsonSerializer.Serialize(response)).ConfigureAwait(false);
    }
}
