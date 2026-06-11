using SafeIR;

namespace SafeIR.Tests;

public sealed class CompilerTests
{
    [Fact]
    public async Task Compiled_pure_module_matches_interpreter_result()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(7), SandboxValue.FromInt32(3)]);

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.False(string.IsNullOrWhiteSpace(compiled.ArtifactHash));
    }

    [Fact]
    public async Task Auto_mode_falls_back_to_interpreter_for_effectful_modules()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config.json"), "from-file");
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("config.json"));
        var policy = SandboxPolicyBuilder.Create().GrantFileRead(temp.Path, 1024).WithFuel(5_000).Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Auto });

        Assert.True(result.Succeeded);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal("from-file", ((StringValue)result.Value!).Value);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-ir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
