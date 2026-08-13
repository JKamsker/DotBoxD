package dev.dotboxd.rider.run

import com.intellij.execution.ProgramRunnerUtil
import com.intellij.execution.RunManager
import com.intellij.execution.configurations.ConfigurationTypeUtil
import com.intellij.execution.executors.DefaultDebugExecutor
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.progress.Task
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.Messages
import com.intellij.openapi.ui.popup.JBPopupFactory
import com.intellij.ui.ColoredListCellRenderer
import com.intellij.ui.SimpleTextAttributes

class DotBoxDAttachAction : AnAction() {
    override fun update(event: AnActionEvent) {
        event.presentation.isEnabledAndVisible = event.project != null
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = event.project ?: return
        ProgressManager.getInstance().run(object : Task.Backgroundable(project, "Finding DotBoxD kernel debug bridges", true) {
            override fun run(indicator: ProgressIndicator) {
                indicator.isIndeterminate = true
                val descriptors = DotBoxDDiscovery.read()
                ApplicationManager.getApplication().invokeLater { select(project, descriptors) }
            }
        })
    }

    private fun select(project: Project, descriptors: List<DotBoxDBridgeDescriptor>) {
        when (descriptors.size) {
            0 -> Messages.showInfoMessage(
                project,
                "No active DotBoxD kernel debug bridge was found. Start the plugin with DOTBOXD_KERNEL_DEBUG=1.",
                "Attach to DotBoxD Kernels",
            )
            1 -> launch(project, descriptors.single())
            else -> JBPopupFactory.getInstance()
                .createPopupChooserBuilder(descriptors)
                .setTitle("Attach to DotBoxD Kernels")
                .setRenderer(DescriptorRenderer())
                .setItemChosenCallback(com.intellij.util.Consumer { launch(project, it) })
                .createPopup()
                .showCenteredInCurrentWindow(project)
        }
    }

    private fun launch(project: Project, descriptor: DotBoxDBridgeDescriptor) {
        val type = ConfigurationTypeUtil.findConfigurationType(DotBoxDConfigurationType::class.java)
            ?: return Messages.showErrorDialog(project, "DotBoxD's run configuration is unavailable.", "DotBoxD Kernel Debugger")
        val settings = RunManager.getInstance(project)
            .createConfiguration("DotBoxD kernels (PID ${descriptor.processId})", type.configurationFactories.single())
        settings.isTemporary = true
        (settings.configuration as DotBoxDRunConfiguration).processId = descriptor.processId
        ProgramRunnerUtil.executeConfiguration(settings, DefaultDebugExecutor.getDebugExecutorInstance())
    }

    private class DescriptorRenderer : ColoredListCellRenderer<DotBoxDBridgeDescriptor>() {
        override fun customizeCellRenderer(
            list: javax.swing.JList<out DotBoxDBridgeDescriptor>,
            value: DotBoxDBridgeDescriptor?,
            index: Int,
            selected: Boolean,
            hasFocus: Boolean,
        ) {
            if (value == null) return
            append("PID ${value.processId}", SimpleTextAttributes.REGULAR_ATTRIBUTES)
        }
    }
}
