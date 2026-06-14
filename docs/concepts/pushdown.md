# Pushdown

**Pushdown** turns many small remote service calls into **one** validated server-side execution. A
kernel composes the host's own service surface *next to the data*, so the client submits the work once
instead of making N round-trips.

Without pushdown:

```text
client -> GetUnitPrice(item1)
client -> GetUnitPrice(item2)
...           (N round-trips)
```

With pushdown:

```text
client -> submit validated kernel once
host   -> runs the kernel locally against the service + its data
host   -> returns one compact result
```

The bridge lives in `DotBoxD.Pushdown.Services` (a MessagePack IPC addon that composes kernels with
services). It is the direct realization of the merge: the **Services** (RPC) and **Kernels** (sandbox)
stacks compiling and running together.

Pushdown executes kernels under the same validation + metering as any other kernel, and should run under
its own capability policy — a method reachable via normal RPC is not automatically reachable from a
kernel.

**See also:** the runnable [`samples/Pushdown/DotBoxD.EndToEnd`](../../samples/Pushdown/DotBoxD.EndToEnd)
(prints the round-trip win) and [`samples/Pushdown/PluginIpc`](../../samples/Pushdown/PluginIpc).
Roadmap items (`DotBoxD.Pushdown.Linq`, fluent client API) are tracked in
[follow-up-issues](../architecture/follow-up-issues.md).
