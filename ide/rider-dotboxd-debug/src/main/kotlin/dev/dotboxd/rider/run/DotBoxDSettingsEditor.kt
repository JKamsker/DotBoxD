package dev.dotboxd.rider.run

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.options.SettingsEditor
import com.intellij.openapi.ui.ComboBox
import com.intellij.openapi.ui.Messages
import com.intellij.ui.components.JBTextField
import com.intellij.util.ui.FormBuilder
import java.awt.BorderLayout
import javax.swing.DefaultComboBoxModel
import javax.swing.JButton
import javax.swing.JComponent
import javax.swing.JPanel

class DotBoxDSettingsEditor : SettingsEditor<DotBoxDRunConfiguration>() {
    private val process = ComboBox<DotBoxDBridgeDescriptor>()
    private val discover = JButton("Refresh")
    private val pluginId = JBTextField()
    private val pauseScope = ComboBox(DotBoxDRunConfiguration.PAUSE_SCOPES.toTypedArray())
    private val adapterPath = JBTextField()

    override fun createEditor(): JComponent {
        process.renderer = DescriptorRenderer()
        discover.addActionListener { refresh(showEmptyMessage = true) }
        val processPanel = JPanel(BorderLayout(8, 0)).apply {
            add(process, BorderLayout.CENTER)
            add(discover, BorderLayout.EAST)
        }
        return FormBuilder.createFormBuilder()
            .addLabeledComponent("Plugin process", processPanel)
            .addLabeledComponent("Package filter", pluginId)
            .addLabeledComponent("Pause scope", pauseScope)
            .addLabeledComponent("Adapter DLL override", adapterPath)
            .addComponentFillVertically(JPanel(), 0)
            .panel
    }

    override fun resetEditorFrom(configuration: DotBoxDRunConfiguration) {
        refresh(selectedProcessId = configuration.processId)
        pluginId.text = configuration.pluginId
        pauseScope.selectedItem = configuration.pauseScope
        adapterPath.text = configuration.adapterPath
    }

    override fun applyEditorTo(configuration: DotBoxDRunConfiguration) {
        configuration.processId = (process.selectedItem as? DotBoxDBridgeDescriptor)?.processId ?: 0
        configuration.pluginId = pluginId.text.trim()
        configuration.pauseScope = pauseScope.selectedItem as? String
            ?: DotBoxDRunConfiguration.HOST_DEFAULT_PAUSE_SCOPE
        configuration.adapterPath = adapterPath.text.trim()
    }

    private fun refresh(selectedProcessId: Int = (process.selectedItem as? DotBoxDBridgeDescriptor)?.processId ?: 0, showEmptyMessage: Boolean = false) {
        discover.isEnabled = false
        ApplicationManager.getApplication().executeOnPooledThread {
            val descriptors = DotBoxDDiscovery.read()
            ApplicationManager.getApplication().invokeLater {
                process.model = DefaultComboBoxModel(descriptors.toTypedArray())
                process.selectedItem = descriptors.firstOrNull { it.processId == selectedProcessId }
                    ?: descriptors.firstOrNull()
                discover.isEnabled = true
                if (showEmptyMessage && descriptors.isEmpty()) {
                    Messages.showInfoMessage(
                        process,
                        "No active DotBoxD kernel debug bridge was found. Start the plugin with DOTBOXD_KERNEL_DEBUG=1.",
                        "DotBoxD Kernel Debugger",
                    )
                }
            }
        }
    }

    private class DescriptorRenderer : com.intellij.ui.ColoredListCellRenderer<DotBoxDBridgeDescriptor>() {
        override fun customizeCellRenderer(
            list: javax.swing.JList<out DotBoxDBridgeDescriptor>,
            value: DotBoxDBridgeDescriptor?,
            index: Int,
            selected: Boolean,
            hasFocus: Boolean,
        ) {
            if (value == null) return
            append("PID ${value.processId}")
        }
    }
}
