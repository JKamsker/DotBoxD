---
title: 'Tutorials & walkthroughs'
description: >-
  One from-scratch tutorial (call the host with Services) plus three guided walkthroughs of the
  maintained GameServer sample (react with event pipelines, extend with Pushdown, and hand-written
  IR).
---
Four learning pages, in recommended order, one per interaction mode - *call*, *react*, *extend* -
plus an advanced no-generator path. Naming them honestly: **only #1 is a from-scratch tutorial**
that ends with your own running project. Pages 2–4 are **guided walkthroughs** of the maintained
GameServer sample: you clone the repo and read/run along against real, tested code, because their
snippets build on sample-only types. Independently runnable starters for the react/extend modes
are on the roadmap; until then the walkthroughs are the supported path.

| # | Page | Format | You end up with | Starts from |
|---|------|--------|-----------------|-------------|
| 1 | [First Service (RPC)](/tutorials/first-service/) — *call the host* | Tutorial | Your own contract, host, and client with a generated typed proxy | An empty project |
| 2 | [Event pipelines (RunLocal)](/tutorials/event-pipeline-runlocal/) — *react to the host* | Guided walkthrough | A plugin whose `Where`/`Select` filter runs server-side | The [GameServer sample](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer) (**clone the repo**) |
| 3 | [Pushdown server extension](/tutorials/pushdown-server-extension/) — *extend the host* | Guided walkthrough | A plugin-shipped, server-side batch operation | The GameServer sample (**clone the repo**) |
| 4 | [Hand-written IR hook pipeline](/tutorials/handwritten-ir-hook-pipeline/) — *advanced* | Guided walkthrough | The same shapes from public primitives, with no generator | Public primitives; snippets use GameServer sample types (read along) |

> **Clone first for 2–4.** Tutorial 1 builds from an empty project. The other pages run against
> the maintained
> [GameServer sample](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer):
> Pushdown's final step installs into the sample's `PluginServer`, and the event-pipeline snippets
> reference sample-only types that will not compile in an empty project — so clone the repo and
> read/run along.

## Why each page exists

1. **[First Service (RPC)](/tutorials/first-service/)** — define a `[RpcService]` contract, host it, and
   call it from a client over a typed proxy. *Why this mode:* easy interop — one C# contract compiles
   to a typed proxy + dispatcher, so there is no hand-written marshaling and no runtime reflection on
   the hot path; the interface is the single source of truth. Unity/IL2CPP additionally requires
   generated codec formatters and validation in the consumer's own build.
2. **[Event pipelines (RunLocal)](/tutorials/event-pipeline-runlocal/)** — subscribe to a server event, push the
   `Where`/`Select` filter server-side, and react locally with `RunLocal` so only matching, projected
   data crosses the pipe. *Why this mode:* efficient server-side filtering + projection — `Where`/`Select`
   lower to verified restricted IR (intermediate representation), so only matching, projected values
   cross the pipe (fewer bytes, fewer wake-ups, one-way push, no round-trips), and that filter logic is
   safe to accept from untrusted plugins because it runs as validated, fuel-metered IR.
3. **[Pushdown server extension](/tutorials/pushdown-server-extension/)** — ship a plugin-supplied, server-side
   batch operation that collapses N remote calls into one. *Why this mode:* reduce round-trips — move the
   loop/aggregation next to the data (N calls → 1 server-side batch); the host stays frozen/minimal while
   plugins add batch ops without recompiling it, and the batch runs as verified, capability-gated,
   fuel-metered IR.
4. **[Hand-written IR hook pipeline](/tutorials/handwritten-ir-hook-pipeline/)** — build or load a
   `PluginPackage`, install it under policy, and wire it to hooks/subscriptions with public primitives.
   *Why this path:* no lock-in — use it when another language, build step, or custom fluent API emits the IR
   instead of DotBoxD's generator. Skip it until you need it.

New to the project? Read [Getting started](/getting-started/) first - it ends with a complete,
verified five-minute example - then come back here. For the mental model behind the three modes,
see [What is DotBoxD?](/overview/), and for the deep-dives, the [concept pages](/concepts/services/).
