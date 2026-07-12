package dev.dotboxd.rider.run

import com.intellij.openapi.fileTypes.PlainTextFileType
import com.intellij.openapi.vfs.LocalFileSystem
import com.intellij.testFramework.LightVirtualFile
import com.intellij.util.concurrency.AppExecutorUtil
import com.intellij.xdebugger.XDebuggerUtil
import com.intellij.xdebugger.XSourcePosition
import com.intellij.xdebugger.evaluation.XDebuggerEvaluator
import com.intellij.xdebugger.frame.XCompositeNode
import com.intellij.xdebugger.frame.XExecutionStack
import com.intellij.xdebugger.frame.XStackFrame
import com.intellij.xdebugger.frame.XValueChildrenList
import org.eclipse.lsp4j.debug.EvaluateArguments
import org.eclipse.lsp4j.debug.EvaluateArgumentsContext
import org.eclipse.lsp4j.debug.ScopesArguments
import org.eclipse.lsp4j.debug.SourceArguments
import org.eclipse.lsp4j.debug.StackFrame
import org.eclipse.lsp4j.debug.StackTraceArguments
import org.eclipse.lsp4j.debug.services.IDebugProtocolServer

class DapExecutionStack(
    displayName: String,
    private val threadId: Int,
    private val remote: () -> IDebugProtocolServer?,
    private val values: DapValueFactory,
    private val topFrame: XStackFrame? = null,
    internal val topFrameId: Int? = null,
) : XExecutionStack(displayName) {
    override fun getTopFrame(): XStackFrame? = topFrame

    override fun computeStackFrames(firstFrameIndex: Int, container: XStackFrameContainer) {
        AppExecutorUtil.getAppExecutorService().execute {
            try {
                val server = requireNotNull(remote())
                val response = server.stackTrace(StackTraceArguments().apply {
                    threadId = this@DapExecutionStack.threadId
                    startFrame = firstFrameIndex
                    levels = 100
                }).awaitDap()
                val frames = response.stackFrames.orEmpty()
                    .map { DapStackFrame(it, server, values) }
                container.addStackFrames(frames, true)
            } catch (exception: Exception) {
                container.errorOccurred(exception.message ?: "stackTrace failed")
            }
        }
    }

    companion object {
        fun withTopFrame(
            displayName: String,
            threadId: Int,
            remote: () -> IDebugProtocolServer?,
            values: DapValueFactory,
        ): DapExecutionStack {
            val server = requireNotNull(remote())
            val frame = server.stackTrace(StackTraceArguments().apply {
                this.threadId = threadId
                startFrame = 0
                levels = 1
            }).awaitDap().stackFrames.orEmpty().firstOrNull()
            return DapExecutionStack(
                displayName,
                threadId,
                remote,
                values,
                frame?.let { DapStackFrame(it, server, values) },
                frame?.id,
            )
        }
    }
}

private class DapStackFrame(
    private val frame: StackFrame,
    private val remote: IDebugProtocolServer,
    private val values: DapValueFactory,
) : XStackFrame() {
    private val cachedSourcePosition: XSourcePosition? = resolveSourcePosition()

    override fun getSourcePosition() = cachedSourcePosition

    override fun computeChildren(node: XCompositeNode) {
        AppExecutorUtil.getAppExecutorService().execute {
            try {
                val scopes = remote.scopes(ScopesArguments().apply { frameId = frame.id }).awaitDap().scopes.orEmpty()
                if (scopes.isEmpty()) {
                    node.addChildren(XValueChildrenList.EMPTY, true)
                    return@execute
                }
                val children = XValueChildrenList()
                for (scope in scopes) {
                    val response = remote.variables(org.eclipse.lsp4j.debug.VariablesArguments().apply {
                        variablesReference = scope.variablesReference
                    }).awaitDap()
                    response.variables.orEmpty().forEach {
                        children.add(it.name, values.variable(it, scope.variablesReference))
                    }
                }
                node.addChildren(children, true)
            } catch (exception: Exception) {
                node.setErrorMessage(exception.message ?: "scopes failed")
            }
        }
    }

    override fun getEvaluator(): XDebuggerEvaluator = object : XDebuggerEvaluator() {
        override fun evaluate(expression: String, callback: XEvaluationCallback, position: XSourcePosition?) {
            AppExecutorUtil.getAppExecutorService().execute {
                try {
                    val response = remote.evaluate(EvaluateArguments().apply {
                        this.expression = expression
                        frameId = frame.id
                        context = if (position == null) EvaluateArgumentsContext.REPL else EvaluateArgumentsContext.HOVER
                    }).awaitDap()
                    callback.evaluated(values.evaluated(response))
                } catch (exception: Exception) {
                    callback.errorOccurred(exception.message ?: "evaluate failed")
                }
            }
        }
    }

    private fun resolveSourcePosition(): XSourcePosition? {
        val source = frame.source ?: return null
        val path = source.path ?: return null
        val file = if (path.startsWith("dotboxd-ir://", ignoreCase = true)) {
            val response = remote.source(SourceArguments().apply {
                this.source = source
                sourceReference = source.sourceReference
            }).awaitDap()
            LightVirtualFile(source.name ?: "DotBoxD IR", PlainTextFileType.INSTANCE, response.content)
        } else {
            LocalFileSystem.getInstance().refreshAndFindFileByPath(path) ?: return null
        }
        return XDebuggerUtil.getInstance().createPosition(file, (frame.line - 1).coerceAtLeast(0))
    }
}
