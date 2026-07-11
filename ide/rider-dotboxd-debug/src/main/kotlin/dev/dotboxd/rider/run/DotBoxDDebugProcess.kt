package dev.dotboxd.rider.run

import com.intellij.execution.process.ProcessHandler
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.logger
import com.intellij.openapi.fileTypes.PlainTextFileType
import com.intellij.util.concurrency.AppExecutorUtil
import com.intellij.xdebugger.XDebugProcess
import com.intellij.xdebugger.XDebugSession
import com.intellij.xdebugger.XSourcePosition
import com.intellij.xdebugger.breakpoints.XBreakpointHandler
import com.intellij.xdebugger.evaluation.EvaluationMode
import com.intellij.xdebugger.evaluation.XDebuggerEditorsProvider
import com.intellij.xdebugger.evaluation.XDebuggerEvaluator
import com.intellij.xdebugger.frame.XExecutionStack
import com.intellij.xdebugger.frame.XSuspendContext
import org.eclipse.lsp4j.debug.ContinueArguments
import org.eclipse.lsp4j.debug.DisconnectArguments
import org.eclipse.lsp4j.debug.EvaluateArguments
import org.eclipse.lsp4j.debug.EvaluateArgumentsContext
import org.eclipse.lsp4j.debug.NextArguments
import org.eclipse.lsp4j.debug.PauseArguments
import org.eclipse.lsp4j.debug.StackTraceArguments
import org.eclipse.lsp4j.debug.StepInArguments
import org.eclipse.lsp4j.debug.StepOutArguments
import org.eclipse.lsp4j.debug.StoppedEventArguments
import org.eclipse.lsp4j.debug.launch.DSPLauncher
import org.eclipse.lsp4j.debug.services.IDebugProtocolServer
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ExecutionException
import java.util.concurrent.Executors
import java.util.concurrent.TimeoutException
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

class DotBoxDDebugProcess(
    session: XDebugSession,
    private val handler: DapProcessHandler,
    private val configuration: DotBoxDRunConfiguration,
) : XDebugProcess(session) {
    private val log = logger<DotBoxDDebugProcess>()
    private val remote = AtomicReference<IDebugProtocolServer?>()
    private val adapterAttached = AtomicBoolean()
    private val initializedEventPending = AtomicBoolean()
    private val stoppedExecution = StoppedExecutionState()
    private val stoppedEvents = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, "DotBoxD stopped-event resolver").apply { isDaemon = true }
    }
    private val console = com.intellij.execution.filters.TextConsoleBuilderFactory.getInstance()
        .createBuilder(configuration.project).console
    private val values = DapValueFactory(remote::get, session::rebuildViews)
    private val breakpoints = DotBoxDBreakpoints(session) {
        remote.get().takeIf { adapterAttached.get() }
    }
    private val client = DotBoxDDapClient(
        onInitialized = ::adapterInitialized,
        onStopped = ::stopped,
        onOutput = { console.print(it.output.orEmpty(), outputType(it.category)) },
        onTerminated = { ApplicationManager.getApplication().invokeLater(session::stop) },
    )

    init {
        AppExecutorUtil.getAppExecutorService().execute(::connect)
    }

    override fun sessionInitialized() = session.setPauseActionSupported(true)
    override fun createConsole(): com.intellij.execution.ui.ExecutionConsole = console
    override fun doGetProcessHandler(): ProcessHandler = handler
    override fun getBreakpointHandlers(): Array<XBreakpointHandler<*>> = breakpoints.handlers()
    override fun getEditorsProvider(): XDebuggerEditorsProvider = EditorsProvider

    override fun resume(context: XSuspendContext?) = control { continue_(ContinueArguments().apply { threadId = it }) }
    override fun startPausing() = pause()
    override fun startStepOver(context: XSuspendContext?) = control { next(NextArguments().apply { threadId = it }) }
    override fun startStepInto(context: XSuspendContext?) = control { stepIn(StepInArguments().apply { threadId = it }) }
    override fun startStepOut(context: XSuspendContext?) = control { stepOut(StepOutArguments().apply { threadId = it }) }

    override fun getEvaluator(): XDebuggerEvaluator = object : XDebuggerEvaluator() {
        override fun evaluate(expression: String, callback: XEvaluationCallback, position: XSourcePosition?) {
            AppExecutorUtil.getAppExecutorService().execute {
                try {
                    val server = requireNotNull(remote.get())
                    val threadId = stoppedExecution.current()
                    if (threadId == null) {
                        callback.errorOccurred("No kernel execution is stopped.")
                        return@execute
                    }

                    val frameId = server.stackTrace(StackTraceArguments().apply {
                        this.threadId = threadId
                        levels = 1
                    }).awaitDap().stackFrames?.firstOrNull()?.id
                    val response = server.evaluate(EvaluateArguments().apply {
                        this.expression = expression
                        this.frameId = frameId
                        context = EvaluateArgumentsContext.REPL
                    }).awaitDap()
                    callback.evaluated(values.evaluated(response))
                } catch (exception: Exception) {
                    callback.errorOccurred(exception.message ?: "evaluate failed")
                }
            }
        }
    }

    override fun stop() {
        stoppedExecution.clear()
        stoppedEvents.shutdownNow()
        val server = remote.getAndSet(null)
        AppExecutorUtil.getAppExecutorService().execute {
            runCatching { server?.disconnect(DisconnectArguments().apply { terminateDebuggee = false })?.awaitDap() }
                .onFailure { log.warn("DotBoxD DAP disconnect failed", it) }
            handler.destroyProcess()
        }
    }

    private fun connect() {
        try {
            val process = handler.process()
            val launcher = DSPLauncher.createClientLauncher(client, process.inputStream, process.outputStream)
            val server = launcher.remoteProxy
            remote.set(server)
            launcher.startListening()
            server.initialize(DotBoxDDapClient.initializeArguments()).awaitDap()
            server.attach(linkedMapOf<String, Any>("processId" to configuration.processId).apply {
                configuration.pauseScope
                    .takeUnless { it == DotBoxDRunConfiguration.HOST_DEFAULT_PAUSE_SCOPE }
                    ?.let { put("pauseScope", it) }
                configuration.pluginId.takeIf(String::isNotBlank)?.let { put("pluginId", it) }
            }).awaitDap()
            adapterAttached.set(true)
            synchronizeBreakpointsIfReady()
        } catch (exception: Exception) {
            adapterAttached.set(false)
            remote.set(null)
            log.warn("DotBoxD DAP connection failed", exception)
            ApplicationManager.getApplication().invokeLater {
                session.reportError("DotBoxD kernel debugger: ${connectionFailureMessage(exception)}")
                session.stop()
            }
        }
    }

    private fun adapterInitialized() {
        initializedEventPending.set(true)
        synchronizeBreakpointsIfReady()
    }

    private fun synchronizeBreakpointsIfReady() {
        if (adapterAttached.get() && initializedEventPending.compareAndSet(true, false)) {
            breakpoints.pushAll(::configurationDone)
        }
    }

    private fun configurationDone() {
        try {
            remote.get()?.configurationDone(org.eclipse.lsp4j.debug.ConfigurationDoneArguments())?.awaitDap()
        } catch (exception: Exception) {
            log.warn("DotBoxD DAP configuration failed", exception)
            ApplicationManager.getApplication().invokeLater {
                session.reportError("DotBoxD kernel debugger: ${connectionFailureMessage(exception)}")
                session.stop()
            }
        }
    }

    private fun stopped(arguments: StoppedEventArguments) {
        val threadId = arguments.threadId?.takeIf { it > 0 } ?: return
        stoppedEvents.execute {
            val displayName = "Kernel execution $threadId"
            val stack = runCatching {
                DapExecutionStack.withTopFrame(displayName, threadId, remote::get, values)
            }.getOrElse {
                log.warn("DotBoxD stopped stack resolution failed", it)
                DapExecutionStack(displayName, threadId, remote::get, values)
            }
            stoppedExecution.stopped(threadId)
            session.positionReached(DapSuspendContext(threadId, stack))
        }
    }

    private inner class DapSuspendContext(
        private val threadId: Int,
        private val activeStack: DapExecutionStack,
    ) : XSuspendContext() {
        override fun getActiveExecutionStack(): XExecutionStack = activeStack

        override fun computeExecutionStacks(container: XExecutionStackContainer) {
            AppExecutorUtil.getAppExecutorService().execute {
                try {
                    val stacks = requireNotNull(remote.get()).threads().awaitDap().threads.orEmpty().map {
                        DapExecutionStack(it.name, it.id, remote::get, values)
                    }
                    container.addExecutionStack(stacks, true)
                } catch (exception: Exception) {
                    container.errorOccurred(exception.message ?: "threads failed")
                }
            }
        }
    }

    private fun control(command: IDebugProtocolServer.(Int) -> CompletableFuture<*>) {
        val threadId = stoppedExecution.claim() ?: return
        val server = remote.get() ?: run {
            stoppedExecution.restore(threadId)
            return
        }
        AppExecutorUtil.getAppExecutorService().execute {
            try {
                server.command(threadId).awaitDap()
            } catch (exception: Exception) {
                stoppedExecution.restore(threadId)
                log.warn("DotBoxD DAP control command failed", exception)
                ApplicationManager.getApplication().invokeLater {
                    session.reportError("DotBoxD kernel debugger: ${connectionFailureMessage(exception)}")
                }
            }
        }
    }

    private fun pause() {
        val server = remote.get() ?: return
        AppExecutorUtil.getAppExecutorService().execute {
            try {
                server.pause(PauseArguments().apply { threadId = stoppedExecution.current() ?: 0 }).awaitDap()
            } catch (exception: Exception) {
                log.warn("DotBoxD DAP pause failed", exception)
                ApplicationManager.getApplication().invokeLater {
                    session.reportError("DotBoxD kernel debugger: ${connectionFailureMessage(exception)}")
                }
            }
        }
    }

    private fun outputType(category: String?) = when (category) {
        "stderr", "important" -> com.intellij.execution.ui.ConsoleViewContentType.ERROR_OUTPUT
        "console" -> com.intellij.execution.ui.ConsoleViewContentType.SYSTEM_OUTPUT
        else -> com.intellij.execution.ui.ConsoleViewContentType.NORMAL_OUTPUT
    }

    private fun connectionFailureMessage(exception: Exception): String {
        val failure = if (exception is ExecutionException) exception.cause ?: exception else exception
        return when (failure) {
            is TimeoutException -> "adapter request timed out"
            else -> failure.message?.takeIf(String::isNotBlank) ?: failure.javaClass.simpleName
        }
    }

    private object EditorsProvider : XDebuggerEditorsProvider() {
        override fun getFileType() = PlainTextFileType.INSTANCE
        override fun createDocument(
            project: com.intellij.openapi.project.Project,
            expression: com.intellij.xdebugger.XExpression,
            sourcePosition: XSourcePosition?,
            mode: EvaluationMode,
        ) = com.intellij.openapi.editor.EditorFactory.getInstance().createDocument(expression.expression)
    }
}
