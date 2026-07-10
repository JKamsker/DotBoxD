# DotBoxD Kernel Debugger for VS Code

This minimal debugger extension launches the repository's .NET DAP adapter and attaches it to the protected
`PluginDebugBridge` owned by a plugin process. It does not move execution into the plugin: `RunLocal` and
`RegisterLocal` remain ordinary managed-debugger code, while kernel stops are the actual server interpreter.

Build `tools/DotBoxD.DebugAdapter`, install this folder as a development extension, then launch the plugin with
`DOTBOXD_KERNEL_DEBUG=1`. Use **DotBoxD: Pick Kernel Debug Process** or the supplied `dotboxd-kernel` attach
snippet. `pluginId` is optional; omitting it binds breakpoints across every mapped package in the session.

The repository's `.vscode/launch.json` demonstrates the complete GameServer compound.
