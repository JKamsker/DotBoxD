# Tutorials

Three end-to-end walkthroughs, one per mode. Each starts from an empty project and ends with
something you can run. Each mode exists for a different reason — pick by what you need before you
click in:

1. **[First Service (RPC)](first-service.md)** — define a `[DotBoxDService]` contract, host it, and
   call it from a client over a typed proxy. *Why this mode:* easy interop — one C# contract compiles
   to a typed proxy + dispatcher, so there is no hand-written marshaling and no runtime reflection on
   the hot path (AOTs, runs on Unity/IL2CPP); the interface is the single source of truth.
2. **[Event pipelines (RunLocal)](event-pipeline-runlocal.md)** — subscribe to a server event, push the
   `Where`/`Select` filter server-side, and react locally with `RunLocal` so only matching, projected
   data crosses the pipe. *Why this mode:* efficient server-side filtering + projection — `Where`/`Select`
   lower to verified IR, so only matching, projected values cross the pipe (fewer bytes, fewer wake-ups,
   one-way push, no round-trips), and that filter logic is safe to accept from untrusted plugins because
   it runs as validated, fuel-metered IR.
3. **[Pushdown server extension](pushdown-server-extension.md)** — ship a plugin-supplied, server-side
   batch operation that collapses N remote calls into one. *Why this mode:* reduce round-trips — move the
   loop/aggregation next to the data (N calls → 1 server-side batch); the host stays frozen/minimal while
   plugins add batch ops without recompiling it, and the batch runs as verified, capability-gated,
   fuel-metered IR.

New to the project? Read [Getting started](../getting-started/README.md) first, then come back here.
For the concepts behind each mode, see the [Guide](../index.md).
