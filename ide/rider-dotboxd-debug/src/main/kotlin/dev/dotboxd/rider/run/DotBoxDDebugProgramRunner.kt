package dev.dotboxd.rider.run

import com.intellij.execution.ExecutionException
import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.configurations.RunProfile
import com.intellij.execution.configurations.RunProfileState
import com.intellij.execution.configurations.RunnerSettings
import com.intellij.execution.executors.DefaultDebugExecutor
import com.intellij.execution.runners.ExecutionEnvironment
import com.intellij.execution.runners.GenericProgramRunner
import com.intellij.execution.ui.RunContentDescriptor
import com.intellij.openapi.project.Project
import com.intellij.xdebugger.XDebugProcessStarter
import com.intellij.xdebugger.XDebugSession
import com.intellij.xdebugger.XDebuggerManager
import java.io.File
import java.io.InputStream
import java.net.URL
import java.security.MessageDigest

class DotBoxDDebugProgramRunner : GenericProgramRunner<RunnerSettings>() {
    override fun getRunnerId() = "DotBoxDKernelDebugProgramRunner"

    override fun canRun(executorId: String, profile: RunProfile) =
        executorId == DefaultDebugExecutor.EXECUTOR_ID && profile is DotBoxDRunConfiguration

    @Suppress("OVERRIDE_DEPRECATION", "DEPRECATION")
    override fun doExecute(
        project: Project,
        state: RunProfileState,
        contentToReuse: RunContentDescriptor?,
        environment: ExecutionEnvironment,
    ): RunContentDescriptor? {
        val configuration = environment.runProfile as DotBoxDRunConfiguration
        val adapter = resolveAdapter(configuration)
        val commandLine = GeneralCommandLine("dotnet", adapter.absolutePath)
            .withParentEnvironmentType(GeneralCommandLine.ParentEnvironmentType.CONSOLE)
        project.basePath?.let { commandLine.workDirectory = File(it) }
        val stderr = File(com.intellij.openapi.application.PathManager.getLogPath(), "dotboxd-kernel-debug-adapter.log")
        val process = commandLine.toProcessBuilder()
            .redirectErrorStream(false)
            .redirectError(ProcessBuilder.Redirect.appendTo(stderr))
            .start()
        val handler = DapProcessHandler(process)
        com.intellij.util.concurrency.AppExecutorUtil.getAppExecutorService().execute {
            val exitCode = process.waitFor()
            handler.terminated(exitCode)
        }
        val session = XDebuggerManager.getInstance(project).startSession(
            environment,
            object : XDebugProcessStarter() {
                override fun start(session: XDebugSession) = DotBoxDDebugProcess(session, handler, configuration)
            },
        )
        handler.startNotify()
        return session.runContentDescriptor
    }

    private fun resolveAdapter(configuration: DotBoxDRunConfiguration): File {
        if (configuration.adapterPath.isNotBlank()) return File(configuration.adapterPath)
        val resource = javaClass.classLoader.getResource("adapter/DotBoxD.DebugAdapter.dll")
            ?: throw ExecutionException("The Rider plugin does not contain DotBoxD.DebugAdapter.dll.")
        if (resource.protocol == "file") return File(resource.toURI())
        val plugin = com.intellij.ide.plugins.PluginManagerCore.getPlugin(
            com.intellij.openapi.extensions.PluginId.getId("dev.dotboxd.kernel-debug"),
        )
        val version = plugin?.version?.replace(Regex("[^A-Za-z0-9._-]"), "_") ?: "development"
        val output = File(com.intellij.openapi.application.PathManager.getTempPath(), "dotboxd-rider/$version/adapter")
        val marker = File(output, "DotBoxD.DebugAdapter.dll")
        if (!marker.isFile || !resourceMatches(resource, marker)) extractAdapter(output)
        return marker
    }

    private fun resourceMatches(resource: URL, marker: File): Boolean =
        resource.openStream().use(::sha256).contentEquals(marker.inputStream().use(::sha256))

    private fun sha256(input: InputStream): ByteArray {
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
        while (true) {
            val read = input.read(buffer)
            if (read < 0) return digest.digest()
            digest.update(buffer, 0, read)
        }
    }

    private fun extractAdapter(output: File) {
        output.mkdirs()
        val connection = javaClass.classLoader.getResource("adapter/DotBoxD.DebugAdapter.dll")?.openConnection()
        if (connection !is java.net.JarURLConnection) {
            throw ExecutionException("The packaged DotBoxD debug adapter could not be opened.")
        }
        connection.jarFile.use { jar ->
            jar.entries().asSequence().filter { !it.isDirectory && it.name.startsWith("adapter/") }.forEach { entry ->
                val target = File(output, entry.name.removePrefix("adapter/"))
                jar.getInputStream(entry).use { input -> target.outputStream().use(input::copyTo) }
            }
        }
    }
}
