package dev.dotboxd.rider.run

import com.intellij.openapi.application.ApplicationManager
import com.intellij.util.concurrency.AppExecutorUtil
import com.intellij.xdebugger.frame.XCompositeNode
import com.intellij.xdebugger.frame.XValue
import com.intellij.xdebugger.frame.XValueChildrenList
import com.intellij.xdebugger.frame.XValueNode
import com.intellij.xdebugger.frame.XValuePlace
import org.eclipse.lsp4j.debug.SetVariableArguments
import org.eclipse.lsp4j.debug.Variable
import org.eclipse.lsp4j.debug.VariablesArguments
import org.eclipse.lsp4j.debug.services.IDebugProtocolServer

class DapValueFactory(
    private val remote: () -> IDebugProtocolServer?,
    private val rebuildViews: () -> Unit,
) {
    fun variable(value: Variable, parentReference: Int = 0): XValue = object : XValue() {
        override fun computePresentation(node: XValueNode, place: XValuePlace) {
            node.setPresentation(null, value.type.orEmpty(), value.value.orEmpty(), value.variablesReference > 0)
        }

        override fun computeChildren(node: XCompositeNode) {
            children(value.variablesReference, node)
        }

        override fun getModifier(): com.intellij.xdebugger.frame.XValueModifier? {
            if (parentReference <= 0) return null
            return object : com.intellij.xdebugger.frame.XValueModifier() {
                override fun getInitialValueEditorText() = value.value.orEmpty()

                override fun setValue(expression: com.intellij.xdebugger.XExpression, callback: XModificationCallback) {
                    val newValue = expression.expression
                    AppExecutorUtil.getAppExecutorService().execute {
                        try {
                            val response = requireNotNull(remote()).setVariable(SetVariableArguments().apply {
                                variablesReference = parentReference
                                name = value.name
                                this.value = newValue
                            }).awaitDap()
                            value.value = response.value
                            value.type = response.type
                            callback.valueModified()
                            ApplicationManager.getApplication().invokeLater(rebuildViews)
                        } catch (exception: Exception) {
                            callback.errorOccurred(exception.message ?: "setVariable failed")
                        }
                    }
                }
            }
        }
    }

    fun evaluated(value: org.eclipse.lsp4j.debug.EvaluateResponse): XValue = object : XValue() {
        override fun computePresentation(node: XValueNode, place: XValuePlace) {
            node.setPresentation(null, value.type.orEmpty(), value.result.orEmpty(), value.variablesReference > 0)
        }

        override fun computeChildren(node: XCompositeNode) {
            children(value.variablesReference, node)
        }
    }

    private fun children(reference: Int, node: XCompositeNode) {
        if (reference <= 0) {
            node.addChildren(XValueChildrenList.EMPTY, true)
            return
        }
        AppExecutorUtil.getAppExecutorService().execute {
            try {
                val response = requireNotNull(remote()).variables(VariablesArguments().apply {
                    variablesReference = reference
                }).awaitDap()
                val children = XValueChildrenList()
                response.variables.orEmpty().forEach { children.add(it.name, variable(it, reference)) }
                node.addChildren(children, true)
            } catch (exception: Exception) {
                node.setErrorMessage(exception.message ?: "variables failed")
            }
        }
    }
}
