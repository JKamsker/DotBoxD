# Sandboxed UI process sample

This headless sample proves the renderer-neutral foundation of
[issue #1453](https://github.com/JKamsker/DotBoxD/issues/1453).

```text
trusted Host process                  separate Plugin process
UiPackage import/validation           CounterComponent creates data + restricted IR
SandboxHost interpreter  <-- package -- GetPackageAsync
UiSession authoritative state
HeadlessRenderer                      arbitrary C# search handler
SearchTransport  --- typed RPC --->   SearchAsync
                 <-- versioned result --
```

The host never loads plugin executable code. Its project references only trusted UI runtime
and shared RPC contracts; the worker implementation is launched as a different executable.
An explicit assembly check enforces this property at runtime. The headless renderer is trusted
host code. An Avalonia adapter and graphics integration are later stages; this sample does not
render pixels or expose textures/native handles.

Build and run from the repository root:

```bash
dotnet build DotBoxD.slnx -c Release
dotnet samples/SandboxedUi/Host/bin/Release/net10.0/Examples.SandboxedUi.Host.dll samples/SandboxedUi/Plugin/bin/Release/net10.0/Examples.SandboxedUi.Plugin.dll
```

The host starts the worker on a fresh pipe with bounded frames, installs its package, increments
the Counter with no remote dispatch, edits the two-way TextBox, calls remote Search, and checks
that the renderer materialized only once. It then kills the worker and proves that the next call
disconnects/releases the UI while the host remains alive. Success prints a `PASS:` line and exits 0.
The smoke runs on both CI operating systems.

`CounterComponent.Counter()` and `Search()` show reusable C# composition through public schema
primitives. The worker constructs restricted IR directly here; future source authoring can lower
C# into the same public `UiKernel` contract. The search implementation uses ordinary arbitrary
C# exclusively in the worker. This demonstration uses process separation; production hosts must
configure reduced worker OS permissions themselves.

See [schema, threat model, version semantics, limits, and follow-ups](../../docs/sandboxed-ui.md).
