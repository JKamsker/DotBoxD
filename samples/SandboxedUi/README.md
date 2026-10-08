# Sandboxed UI process sample

The sample implements the reference workflow from [issue #1453](https://github.com/JKamsker/DotBoxD/issues/1453).

```text
trusted Host process                         separate Plugin process
AvaloniaUiRenderer + offscreen bitmap         CounterComponent + reusable SearchComponent
UiPackageJson / host policy   <-- package --  UiBuilder + generated local kernel definitions
SandboxHost + ordinary IR validation          compile-time C# -> restricted IR
UiSession authoritative state
local Counter / game.score.read binding       [UiRemoteHandler] arbitrary search C#
SearchTransport              --- typed RPC --> SearchAsync
                             <-- batch result --
```

The host never loads plugin executable code. It references shared contracts and trusted UI libraries;
the worker runs as a separate executable. An explicit assembly check enforces this property. The
Avalonia objects and captured bitmap remain exclusively in the host. No arbitrary AXAML, control
names, delegates, native/GPU handles or texture transfer are part of the wire contract.

Build and run from the repository root:

```bash
dotnet build DotBoxD.slnx -c Release
dotnet samples/SandboxedUi/Host/bin/Release/net10.0/Examples.SandboxedUi.Host.dll samples/SandboxedUi/Plugin/bin/Release/net10.0/Examples.SandboxedUi.Plugin.dll
```

The host starts a worker on a random pipe with bounded frames and generated typed RPC. It installs
the package, increments Counter with no IPC, reads score 42 through the explicit `game.score.read`
capability, edits two-way search text, receives a versioned batch result and captures real Avalonia
pixels on a headless Skia backend. State updates retain the materialized tree. Each session is bound
to its RPC peer's disconnect signal, including closure during installation; normal teardown detaches
the subscription and awaits cleanup. It kills the idle worker and checks session/control release
without a probe RPC, then starts a second worker and installs a fresh session that rejects the old
session ID and repeats remote search to prove reconnect. Success prints `PASS:` and
exits 0. CI runs this on Linux and Windows; separate headless UI tests cover actual focus, keyboard,
mouse, boolean/numeric input, background-thread updates, keyed rows and subscription teardown.

`[UiLocalHandler]` generates public typed kernel definitions using DotBoxD's existing C# lowerer.
`[UiRemoteHandler(7)]` generates an explicit endpoint ID; `SearchTransport` maps it to shared typed
RPC. The search handler executes ordinary C# only in the worker. `GameBindings.ReadScore()` is an
SDK declaration that is lowered to a stable binding ID; the host supplies and authorizes the binding.
Deleting the attributes and handwriting those public definitions/routes is supported and tested.

This demonstrates process separation. Production hosts configure reduced worker OS permissions
and mount the renderer Root in their trusted game/window input and composition pipeline. Offscreen
bitmap capture is a host-only hook; a C++/.NET embedding host decides how to composite/upload it.
Images and resource handles are an explicit post-MVP limitation, as permitted by the issue's DoD.
See [schema, security model, supported C#, ordering and limits](../../docs/sandboxed-ui.md).

## Blazor host

The Interactive Server host reuses the same plugin executable without referencing its implementation
assembly. It displays two independent demo viewer sessions with Counter, Search, and a player score
host binding. Each viewer can disconnect its own worker without affecting the other.

```sh
dotnet run --project samples/SandboxedUi/BlazorHost -c Release -- --urls http://localhost:5078
```

Run the process-boundary, independent-viewer, and reconnect smoke without a browser:

```sh
dotnet run --project samples/SandboxedUi/BlazorHost -c Release -- --smoke
```

The host's explicitly labeled demo identities are for the sample. Production applications supply
their authenticated browser principal and trusted interaction policy. See [sandboxed Blazor](../../docs/sandboxed-blazor.md)
for authorization, circuit lifetime, quota configuration, and the decision to defer safe Razor authoring.
