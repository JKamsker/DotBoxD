---
title: 'Getting started'
description: >-
  Install DotBoxD, run a complete typed RPC example in about five minutes, see the full GameServer
  sample, and pick the journey that matches what you're building.
---
This page gets you from zero to a running, copy-paste-complete example, then routes you by goal.
The mental model behind it all is in [What is DotBoxD?](/overview/) - a two-minute read.

## Prerequisites

- .NET SDK **10.0.2xx** (the repository pins it in `global.json`). The test suite also exercises
  the **.NET 8** and **.NET 9** runtimes, so install those runtimes if you intend to run all tests.
- Any OS: Windows, Linux, macOS.

## First win: a typed RPC call in five minutes

Mode 1 (*call* the host - Services/RPC) gives the fastest end-to-end result: one console project,
one file, a real named-pipe round-trip. Scaffold it:

```bash
dotnet new console -n CatalogQuickstart
cd CatalogQuickstart
dotnet add package DotBoxD --prerelease
```

> `--prerelease` is required while the net10.0 stack is in preview; drop it once a stable tag
> ships. Building for Unity / netstandard2.1 instead? Install `DotBoxD.Services.All` and see
> [Tutorial 1](/tutorials/first-service/) for the transport-explicit setup; the package tables in
> the root [README](https://github.com/JKamsker/DotBoxD/blob/main/README.md) list every individual
> package.

Replace the generated `Program.cs` with this complete file:

```csharp
using DotBoxD.Pushdown.Services;        // RpcMessagePackIpc helper
using DotBoxD.Services.Attributes;      // [RpcService]
using DotBoxD.Services.Generated;       // generated ProvideCatalogService / Get<T>

// A unique pipe name, so parallel runs never collide.
var pipeName = $"dotboxd-quickstart-{Guid.NewGuid():N}";
var prices = new Dictionary<string, int> { ["sword"] = 120, ["shield"] = 80 };

// Host: turn every accepted connection into a peer that serves the contract.
await using var host = RpcMessagePackIpc.ListenNamedPipe(
    pipeName,
    peer => peer.ProvideCatalogService(new CatalogService(prices)));
await host.StartAsync();

// Client: connect and call the generated typed proxy. The client lives in the
// same process here to keep the demo to one file; the call still crosses the
// named pipe exactly like an out-of-process client would.
await using var connection = await RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName);
var catalog = connection.Get<ICatalogService>();

var price = await catalog.GetUnitPriceAsync("sword");
Console.WriteLine($"sword costs {price}");

// The contract: one attribute, no base classes, no marshaling code.
[RpcService]
public interface ICatalogService
{
    ValueTask<int> GetUnitPriceAsync(string itemId, CancellationToken cancellationToken = default);
}

// The host-side implementation is plain C#.
public sealed class CatalogService(Dictionary<string, int> prices) : ICatalogService
{
    public ValueTask<int> GetUnitPriceAsync(string itemId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(prices[itemId]);
}
```

Run it:

```bash
dotnet run
```

Expected output:

```text
sword costs 120
```

What just happened: `[RpcService]` drove a source generator that emitted a typed proxy, a
dispatcher, and the `ProvideCatalogService` / `Get<ICatalogService>()` wiring at compile time,
and the call crossed a real named pipe. No hand-written marshaling, no runtime reflection on the
hot path. Host and client share a process here only to keep the demo to one file - split them
into a host app, a client app, and a shared contract project and nothing about the API changes.
That split, plus MessagePack DTOs, the generated pieces, diagnostics, and the explicit
netstandard2.1/Unity setup, is [Tutorial 1: your first Service](/tutorials/first-service/).

Prefer a ready-made project? The repository ships
[`dotnet new` templates](https://github.com/JKamsker/DotBoxD/tree/main/templates)
(`dotboxd-service`, `dotboxd-sidecar`, `dotboxd-kernel-host`) you can install from a checkout.

## See the whole system run

The maintained GameServer sample ties everything together: service IPC, event kernels, live
settings, host bindings, policy-gated execution, server extensions (Pushdown), and
unload-on-disconnect. It lives in the repo, not the NuGet package - clone and run from the repo
root:

```bash
git clone https://github.com/JKamsker/DotBoxD
cd DotBoxD
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

You should see three phases print - a baseline run, a with-plugins run, and a summary confirming
the plugin's kernels unloaded on disconnect. For the annotated output, see
[What the run prints](/examples/gameserver-walkthrough/#what-the-run-prints). Features no longer
covered by maintained samples are listed in [coverage gaps](/examples/coverage-gaps/).

## Pick your journey

| Your journey | Start here | Then |
|--------------|-----------|------|
| **RPC & Unity** - typed request/response between processes | [Tutorial 1: your first Service](/tutorials/first-service/) (from scratch) | The [RPC & transports deep dive](/channels/quick-start/): [transports](/concepts/channels-transports/), [Unity integration](/channels/unity-integration/), [performance](/channels/performance/). |
| **Plugin author** - extend an existing DotBoxD host | Run the [GameServer baseline](/examples/gameserver-walkthrough/), then walkthrough 2: [event pipelines](/tutorials/event-pipeline-runlocal/) (**clone the repo** - it builds on the sample) | Walkthrough 3: [Pushdown server extension](/tutorials/pushdown-server-extension/), plus the [event pipelines](/concepts/event-pipelines/) and [Pushdown](/concepts/pushdown/) concepts. |
| **Host integrator** - you own the host and want to accept plugins | The [GameServer walkthrough](/examples/gameserver-walkthrough/) server side, then [host bindings](/concepts/host-bindings/) (what you expose) | [Kernel runtime](/concepts/runtime/) (policies, fuel, quotas), then [sandbox caveats](/security/sandbox-caveats/) for production isolation. |
| **Sandbox & tooling author** - hand-written IR, other languages, custom fluent APIs | The [kernels concept](/concepts/kernels/) (includes the smallest end-to-end `SandboxHost`), a real [JSON-IR example](https://github.com/JKamsker/DotBoxD/blob/main/docs/Specs/Initial/dotboxd-sandbox-spec/examples/example-ir.md) | Walkthrough 4: [hand-written IR](/tutorials/handwritten-ir-hook-pipeline/) and the [schemas](/reference/schemas/). |
| **Security reviewer** - decide whether to run untrusted plugins | [Sandbox caveats](/security/sandbox-caveats/) - the three trust postures and what is and isn't a boundary | The [kernel runtime](/concepts/runtime/) for fuel, quotas, and capabilities. |

Unsure which interaction mode fits a call site? Use the decision table in
[Choosing a mode](/overview/#choosing-a-mode).

## Words you'll keep meeting

**Kernel** (the sandboxed unit of plugin logic), **IR** (the restricted instruction format kernels
are written in), **lowering** (compiling your C# down to IR), **fuel** (the execution budget),
**capability** (permission for a kernel to touch a host binding), **terminal** (the last stage of
an event pipeline - where the reaction runs). All defined with links in the
[glossary](/reference/glossary/).

## Next steps

Ready to go deeper? Work through the [tutorials & walkthroughs](/tutorials/) - one per interaction
mode, in order.
