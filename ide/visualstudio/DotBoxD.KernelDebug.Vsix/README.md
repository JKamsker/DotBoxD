# DotBoxD Visual Studio kernel debug engine

This thin VSIX registers `DotBoxD.DebugAdapter` with Visual Studio's Debug Adapter Host. Build the project on
Windows, install the generated VSIX, and launch the GameServer solution profile **GameServer + Plugin + Kernel
Bridge**. In **Debug > Attach to Process**, select the managed plugin process and choose both **Managed (.NET)**
and **DotBoxD Kernel** as code types. Visual Studio then launches the same DAP adapter beside the normal managed
debugger, so C# breakpoints may bind to managed `RunLocal` code and to mapped server-executed IR.

The plugin and server profiles set `DOTBOXD_KERNEL_DEBUG=1`; without that explicit opt-in the debug engine cannot
attach. The launcher passes only the plugin PID. The adapter reads the protected per-user discovery descriptor and
automatically binds source breakpoints across every package owned by that authenticated plugin session.

The trusted CLR evaluators remain host policy choices. Installing this VSIX does not enable them and does not
change the default `SandboxOnly` evaluator.
