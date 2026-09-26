using System.Globalization;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

[Collection(GameServerConsoleCollection.Name)]
public sealed class GameServerCancellationDiagnosticTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_is_not_reported_as_failure(bool invoke)
    {
        using var fixture = new GameServerDiagnosticFixture();
        var package = GameServerDiagnosticFixture.Package();
        var pluginId = invoke ? await fixture.InstallAsync(package) : package.Manifest.PluginId;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var errorOutput = new StringWriter(CultureInfo.InvariantCulture);
        var originalError = Console.Error;
        try
        {
            Console.SetError(errorOutput);
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                if (invoke)
                {
                    await fixture.InvokeAsync(pluginId, KernelRpcBinaryCodec.EncodeArguments([]), cancellation.Token);
                }
                else
                {
                    await fixture.InstallAsync(package, cancellation.Token);
                }
            });

            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Empty(errorOutput.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Non_cancellation_failure_is_logged(bool invoke)
    {
        using var fixture = new GameServerDiagnosticFixture();
        var package = GameServerDiagnosticFixture.Package();
        var pluginId = invoke ? await fixture.InstallAsync(package) : package.Manifest.PluginId;
        if (!invoke)
        {
            fixture.CloseSession();
        }

        using var errorOutput = new StringWriter(CultureInfo.InvariantCulture);
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

            if (invoke)
            {
                Assert.Equal(SandboxErrorCode.InvalidInput, Assert.IsType<SandboxRuntimeException>(error).Error.Code);
            }
            else
            {
                Assert.IsType<ObjectDisposedException>(error);
            }

            Assert.Contains(error.GetType().FullName!, errorOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains(error.Message, errorOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains(invoke ? "invoke failed" : "install failed", errorOutput.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
