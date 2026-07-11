using DotBoxD.Kernels.Game.Server.Simulation;

namespace DotBoxD.Kernels.Game.Server;

/// <summary>Runs repeatable combat rounds at a pace suitable for stepping through installed kernels.</summary>
internal static class ContinuousSimulation
{
    private const int TicksPerRound = 6;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

    public static async Task RunAsync(GameWorld world, GameCommandSink sink)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sink);

        using var stopping = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopping.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            Console.WriteLine("[server] continuous mode active; press Ctrl+C or stop the run configuration to exit.");
            for (var round = 1; !stopping.IsCancellationRequested; round++)
            {
                Console.WriteLine($"--- DEBUG ROUND {round} ---");
                for (var tick = 0; tick < TicksPerRound; tick++)
                {
                    await world.TickAsync(stopping.Token).ConfigureAwait(false);
                    Console.WriteLine($"[tick {world.Tick}]");
                    PrintEffects(sink.DrainEffects());
                    Console.WriteLine(world.Render());
                    await Task.Delay(TickInterval, stopping.Token).ConfigureAwait(false);
                }

                world.Reset();
                sink.DrainEffects();
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            Console.WriteLine("[server] stopping continuous simulation...");
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static void PrintEffects(IReadOnlyList<string> effects)
    {
        if (effects.Count == 0)
        {
            Console.WriteLine("    (no plugin effects applied this tick)");
            return;
        }

        foreach (var effect in effects)
        {
            Console.WriteLine($"    effect: {effect}");
        }
    }
}
