using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;

namespace Examples.SandboxedUi.Contracts;

/// <summary>SDK declaration only; the trusted host supplies the implementation by stable binding ID.</summary>
public static class GameBindings
{
    [HostBinding("ui.game.score", "game.score.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public static int ReadScore() => throw new InvalidOperationException("Only callable from lowered UI logic.");
}
