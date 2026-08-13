# DotBoxD Kernel Debugger for Rider

This Rider plugin launches the same `DotBoxD.DebugAdapter` used by the VS Code and Visual Studio integrations.
It mirrors Rider's ordinary C# line breakpoints into the server-executed kernel session, so a compound run can
debug managed plugin code and mapped kernel IR from the same source files.

Build the plugin with:

```powershell
./gradlew buildPlugin
```

Install the ZIP from `build/distributions` through **Settings > Plugins > Install Plugin from Disk**. Start the
server and plugin with kernel debugging enabled, then use **Run > Attach to Process > Attach to DotBoxD Kernels**.
The picker enumerates live per-user descriptor filenames without opening the descriptors, then passes only the
selected plugin PID to the bundled adapter. A persistent **DotBoxD Kernel** attach configuration can also select a
package filter and pause scope. The bundled framework-dependent adapter requires the .NET 10 runtime.

The host's remote-debug policy remains authoritative. Installing the Rider plugin does not enable remote debugging
or a trusted CLR evaluator.
