package dev.dotboxd.rider.run

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.logger
import com.intellij.util.concurrency.AppExecutorUtil
import com.intellij.xdebugger.XDebugSession
import com.intellij.xdebugger.XDebuggerManager
import com.intellij.xdebugger.XDebuggerUtil
import com.intellij.xdebugger.breakpoints.XBreakpoint
import com.intellij.xdebugger.breakpoints.XBreakpointHandler
import com.intellij.xdebugger.breakpoints.XBreakpointType
import com.intellij.xdebugger.breakpoints.XLineBreakpoint
import com.intellij.xdebugger.breakpoints.SuspendPolicy
import org.eclipse.lsp4j.debug.SetBreakpointsArguments
import org.eclipse.lsp4j.debug.Source
import org.eclipse.lsp4j.debug.SourceBreakpoint
import org.eclipse.lsp4j.debug.services.IDebugProtocolServer
import java.util.concurrent.TimeUnit

class DotBoxDBreakpoints(
    private val session: XDebugSession,
    private val remote: () -> IDebugProtocolServer?,
) {
    private val log = logger<DotBoxDBreakpoints>()

    fun handlers(): Array<XBreakpointHandler<*>> = XDebuggerUtil.getInstance().lineBreakpointTypes
        .map { it.javaClass }
        .distinct()
        .map(::handler)
        .toTypedArray()

    fun pushAll(onComplete: () -> Unit) {
        ApplicationManager.getApplication().invokeLater {
            val grouped = lineBreakpoints().groupBy { it.sourcePosition!!.file.path }
            AppExecutorUtil.getAppExecutorService().execute {
                try {
                    val server = remote() ?: return@execute
                    grouped.forEach { (path, breakpoints) -> push(server, path, breakpoints) }
                } catch (exception: Exception) {
                    log.warn("DotBoxD breakpoint synchronization failed", exception)
                } finally {
                    onComplete()
                }
            }
        }
    }

    @Suppress("UNCHECKED_CAST")
    private fun handler(typeClass: Class<out XBreakpointType<*, *>>): XBreakpointHandler<*> =
        object : XBreakpointHandler<XBreakpoint<*>>(
            typeClass as Class<out XBreakpointType<XBreakpoint<*>, *>>,
        ) {
            override fun registerBreakpoint(breakpoint: XBreakpoint<*>) = changed(breakpoint)
            override fun unregisterBreakpoint(breakpoint: XBreakpoint<*>, temporary: Boolean) = changed(breakpoint)
        }

    private fun changed(breakpoint: XBreakpoint<*>) {
        val line = breakpoint as? XLineBreakpoint<*> ?: return
        val path = line.sourcePosition?.file?.path ?: return
        if (!isCSharp(path)) {
            session.updateBreakpointPresentation(line, null, null)
            return
        }
        ApplicationManager.getApplication().invokeLater {
            val current = lineBreakpoints().filter { it.sourcePosition?.file?.path == path }
            AppExecutorUtil.getAppExecutorService().execute {
                runCatching { remote()?.let { push(it, path, current) } }
                    .onFailure { log.warn("DotBoxD breakpoint update failed for $path", it) }
            }
        }
    }

    private fun lineBreakpoints(): List<XLineBreakpoint<*>> =
        XDebuggerManager.getInstance(session.project).breakpointManager.allBreakpoints
            .mapNotNull { it as? XLineBreakpoint<*> }
            .filter { it.isEnabled && it.sourcePosition?.file?.path?.let(::isCSharp) == true }

    private fun push(server: IDebugProtocolServer, path: String, breakpoints: List<XLineBreakpoint<*>>) {
        val response = server.setBreakpoints(SetBreakpointsArguments().apply {
            source = Source().apply { this.path = path }
            this.breakpoints = breakpoints.map(::sourceBreakpoint).toTypedArray()
        }).get(10, TimeUnit.SECONDS)
        val results = response.breakpoints.orEmpty()
        ApplicationManager.getApplication().invokeLater {
            breakpoints.forEachIndexed { index, breakpoint ->
                val result = results.getOrNull(index)
                if (result?.isVerified == true) session.setBreakpointVerified(breakpoint)
                else session.setBreakpointInvalid(
                    breakpoint,
                    result?.message ?: "The kernel source map did not bind this breakpoint.",
                )
            }
        }
    }

    private fun sourceBreakpoint(breakpoint: XLineBreakpoint<*>) = SourceBreakpoint().apply {
        line = breakpoint.line + 1
        condition = breakpoint.conditionExpression?.expression?.takeIf(String::isNotBlank)
        logMessage = logMessage(breakpoint)
    }

    private fun logMessage(breakpoint: XLineBreakpoint<*>): String? {
        val expression = breakpoint.logExpressionObject?.expression?.takeIf(String::isNotBlank)
        val location = "Breakpoint hit at ${breakpoint.shortFilePath}:${breakpoint.line + 1}"
        return when {
            breakpoint.isLogMessage && expression != null -> "$location: {$expression}"
            breakpoint.isLogMessage -> location
            expression != null -> "{$expression}"
            breakpoint.suspendPolicy == SuspendPolicy.NONE -> location
            else -> null
        }
    }

    companion object {
        fun isCSharp(path: String) = path.endsWith(".cs", ignoreCase = true)
    }
}
