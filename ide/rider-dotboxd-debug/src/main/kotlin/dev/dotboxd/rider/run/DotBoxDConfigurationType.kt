package dev.dotboxd.rider.run

import com.intellij.execution.configurations.ConfigurationFactory
import com.intellij.execution.configurations.ConfigurationType
import com.intellij.icons.AllIcons

class DotBoxDConfigurationType : ConfigurationType {
    private val factory = DotBoxDConfigurationFactory(this)

    override fun getDisplayName() = "DotBoxD Kernel"
    override fun getConfigurationTypeDescription() = "Attach Rider to a local DotBoxD kernel debug bridge."
    override fun getIcon() = AllIcons.Actions.StartDebugger
    override fun getId() = "DotBoxDKernelDebugConfiguration"
    override fun getConfigurationFactories(): Array<ConfigurationFactory> = arrayOf(factory)
}

class DotBoxDConfigurationFactory(type: ConfigurationType) : ConfigurationFactory(type) {
    override fun getId() = "DotBoxDKernelAttach"

    override fun createTemplateConfiguration(project: com.intellij.openapi.project.Project) =
        DotBoxDRunConfiguration(project, this, "Attach to DotBoxD kernels")
}
