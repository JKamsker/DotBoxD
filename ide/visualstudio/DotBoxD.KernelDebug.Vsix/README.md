# DotBoxD Visual Studio kernel debug engine

This VSIX registers `DotBoxD.DebugAdapter` with Visual Studio's Debug Adapter Host. Build the project on Windows,
install the generated VSIX, select the **GameServer + Plugin + Kernel Bridge** solution profile, and press F5.
The extension discovers the opted-in plugin bridge and replaces that process's managed-only attachment with one
containing both **Managed (.NET)** and **DotBoxD Kernel**. The game server remains in the original debug session.
C# breakpoints can therefore bind to managed `RunLocal` code and to mapped server-executed IR without a separate
Attach to Process step.

The plugin and server profiles set `DOTBOXD_KERNEL_DEBUG=1`; without that explicit opt-in the debug engine cannot
attach. The launcher passes only the plugin PID. The adapter reads the protected per-user discovery descriptor and
automatically binds source breakpoints across every package owned by that authenticated plugin session.

The trusted CLR evaluators remain host policy choices. Installing this VSIX does not enable them and does not
change the default `SandboxOnly` evaluator.

Run `eng/scripts/run-vs26-e2e.ps1` on a machine with Visual Studio Community 2026 and the VSIX installed. The test
mirrors the Rider E2E sequence by checking repeated `GuardianKernel` stops at lines 35, 44, 35, and 44.
