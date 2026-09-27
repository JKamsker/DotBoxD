using System.Text;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

[Collection(GameServerConsoleCollection.Name)]
public sealed class GameServerDiagnosticFailureTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Failed_error_output_preserves_the_operation_exception(bool invoke, int failureKind)
    {
        using var fixture = new GameServerDiagnosticFixture();
        var package = GameServerDiagnosticFixture.Package();
        var pluginId = invoke ? await fixture.InstallAsync(package) : package.Manifest.PluginId;
        if (!invoke)
        {
            fixture.CloseSession();
        }

        Exception writeFailure = failureKind switch
        {
            0 => new IOException("simulated stderr write failure"),
            1 => new ObjectDisposedException("diagnostic output"),
            _ => new InvalidOperationException("custom stderr writer failure")
        };
        using var errorOutput = new FailingWriter(writeFailure);
        var originalError = Console.Error;
        try
        {
            Console.SetError(errorOutput);
            var error = await Record.ExceptionAsync(async () =>
            {
                if (invoke)
                {
                    await fixture.InvokeAsync(pluginId, KernelRpcBinaryCodec.EncodeArguments([KernelRpcValue.Int32(1)]));
                }
                else
                {
                    await fixture.InstallAsync(package);
                }
            });

            Assert.NotSame(writeFailure, error);
            if (invoke)
            {
                Assert.Equal(SandboxErrorCode.InvalidInput, Assert.IsType<SandboxRuntimeException>(error).Error.Code);
            }
            else
            {
                Assert.IsType<ObjectDisposedException>(error);
            }

            Assert.Contains(error.Message, errorOutput.LastMessage!, StringComparison.Ordinal);
            Assert.Contains(invoke ? "invoke failed" : "install failed", errorOutput.LastMessage!, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private sealed class FailingWriter(Exception failure) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public string? LastMessage { get; private set; }

        public override void WriteLine(string? value)
        {
            LastMessage = value;
            throw failure;
        }
    }
}
