package dev.dotboxd.rider.run

import org.eclipse.lsp4j.debug.InitializeRequestArguments
import org.eclipse.lsp4j.debug.InitializeRequestArgumentsPathFormat
import org.eclipse.lsp4j.debug.OutputEventArguments
import org.eclipse.lsp4j.debug.StoppedEventArguments
import org.eclipse.lsp4j.debug.services.IDebugProtocolClient

class DotBoxDDapClient(
    private val onInitialized: () -> Unit,
    private val onStopped: (StoppedEventArguments) -> Unit,
    private val onOutput: (OutputEventArguments) -> Unit,
    private val onTerminated: () -> Unit,
) : IDebugProtocolClient {
    override fun initialized() = onInitialized()
    override fun stopped(arguments: StoppedEventArguments) = onStopped(arguments)
    override fun output(arguments: OutputEventArguments) = onOutput(arguments)
    override fun terminated(arguments: org.eclipse.lsp4j.debug.TerminatedEventArguments?) = onTerminated()
    override fun exited(arguments: org.eclipse.lsp4j.debug.ExitedEventArguments?) = onTerminated()

    companion object {
        fun initializeArguments() = InitializeRequestArguments().apply {
            clientID = "dotboxd-rider"
            clientName = "JetBrains Rider"
            adapterID = "dotboxd-kernel"
            linesStartAt1 = true
            columnsStartAt1 = true
            pathFormat = InitializeRequestArgumentsPathFormat.PATH
            supportsRunInTerminalRequest = false
        }
    }
}
