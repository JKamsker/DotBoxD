namespace DotBoxD.Kernels.Tests.Samples.GameServer;

// Console.Error is process-wide; capture and restore it without overlapping other tests.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameServerConsoleCollection
{
    public const string Name = "GameServer console diagnostics";
}
