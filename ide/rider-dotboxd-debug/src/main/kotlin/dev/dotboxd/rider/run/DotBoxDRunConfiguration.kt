package dev.dotboxd.rider.run

import com.intellij.execution.Executor
import com.intellij.execution.configurations.LocatableConfigurationBase
import com.intellij.execution.configurations.RunProfileState
import com.intellij.execution.configurations.RuntimeConfigurationError
import com.intellij.execution.runners.ExecutionEnvironment
import com.intellij.openapi.options.SettingsEditor
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.JDOMExternalizerUtil
import org.jdom.Element

class DotBoxDRunConfiguration(
    project: Project,
    factory: com.intellij.execution.configurations.ConfigurationFactory,
    name: String,
) : LocatableConfigurationBase<DotBoxDRunConfiguration>(project, factory, name) {
    var processId: Int = 0
    var pluginId: String = ""
    var pauseScope: String = HOST_DEFAULT_PAUSE_SCOPE
    var adapterPath: String = ""

    override fun writeExternal(element: Element) {
        super.writeExternal(element)
        JDOMExternalizerUtil.writeField(element, "processId", processId.toString())
        JDOMExternalizerUtil.writeField(element, "pluginId", pluginId)
        JDOMExternalizerUtil.writeField(element, "pauseScope", pauseScope)
        JDOMExternalizerUtil.writeField(element, "adapterPath", adapterPath)
    }

    override fun readExternal(element: Element) {
        super.readExternal(element)
        processId = JDOMExternalizerUtil.readField(element, "processId")?.toIntOrNull() ?: 0
        pluginId = JDOMExternalizerUtil.readField(element, "pluginId") ?: ""
        pauseScope = JDOMExternalizerUtil.readField(element, "pauseScope") ?: HOST_DEFAULT_PAUSE_SCOPE
        adapterPath = JDOMExternalizerUtil.readField(element, "adapterPath") ?: ""
    }

    override fun checkConfiguration() {
        if (processId <= 0) throw RuntimeConfigurationError("Select a running DotBoxD plugin process.")
        if (pauseScope !in PAUSE_SCOPES) throw RuntimeConfigurationError("Pause scope is invalid.")
        if (adapterPath.isNotBlank() && !java.io.File(adapterPath).isFile) {
            throw RuntimeConfigurationError("The configured debug adapter does not exist.")
        }
    }

    override fun getState(executor: Executor, environment: ExecutionEnvironment): RunProfileState =
        DotBoxDDebugRunProfileState()

    override fun getConfigurationEditor(): SettingsEditor<out DotBoxDRunConfiguration> = DotBoxDSettingsEditor()

    companion object {
        const val HOST_DEFAULT_PAUSE_SCOPE = "Host default"
        val PAUSE_SCOPES = listOf(HOST_DEFAULT_PAUSE_SCOPE, "PluginSession", "Execution", "Server")
    }
}

private class DotBoxDDebugRunProfileState : RunProfileState {
    override fun execute(
        executor: Executor?,
        runner: com.intellij.execution.runners.ProgramRunner<*>,
    ): com.intellij.execution.ExecutionResult =
        throw com.intellij.execution.ExecutionException("DotBoxD's debug runner starts this configuration.")
}
