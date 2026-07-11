package dev.dotboxd.rider.e2e

import com.intellij.remoterobot.RemoteRobot
import com.intellij.remoterobot.utils.waitFor
import org.junit.jupiter.api.Test
import java.nio.file.Files
import java.nio.file.Path
import java.time.Duration
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class RiderKernelDebuggerE2ETest {
    private val remoteRobot = RemoteRobot(
        System.getProperty("remote-robot-url", "http://127.0.0.1:8082"),
    )

    @Test
    fun `Rider opens DotBoxD with the kernel debugger plugin`() {
        val rider = RiderDriver(remoteRobot)
        rider.awaitReady()

        assertEquals("DotBoxD", rider.projectName())
        assertTrue(rider.pluginLoaded())
        println(rider.debugSummary())
    }

    @Test
    fun `kernel breakpoints repeat and preserve Where then Run order`() {
        val rider = RiderDriver(remoteRobot)
        rider.awaitReady()
        val root = Path.of(System.getProperty("dotboxd.e2e.root")).toAbsolutePath().normalize()
        val guardian = root.resolve("samples/GameServer/Examples.GameServer.Plugin/Kernels/GuardianKernel.cs")
        val existingBridges = liveBridgeProcessIds()
        val existingExamples = exampleProcessIds(root)
        var launchedExamples = emptyList<Process>()
        try {
            rider.clearDotNetBreakpoints()
            rider.addDotNetBreakpoint(guardian.toString(), 35)
            rider.addDotNetBreakpoint(guardian.toString(), 44)
            launchedExamples = startExamples(rider, root)
            val pluginProcessId = awaitPluginBridge(existingBridges)
            rider.attachToKernels(pluginProcessId)

            val firstPredicate = awaitStop(rider) {
                it.path.endsWith("GuardianKernel.cs") && it.line == 35
            }
            rider.resume()
            val firstHandle = awaitStop(rider) {
                it.path.endsWith("GuardianKernel.cs") && it.line == 44
            }
            rider.resume()
            val repeatedPredicate = awaitStop(rider) {
                it.path.endsWith("GuardianKernel.cs") &&
                    it.line == 35 && it.stackName != firstPredicate.stackName
            }
            rider.resume()
            val repeatedHandle = awaitStop(rider) {
                it.path.endsWith("GuardianKernel.cs") && it.line == 44
            }

            assertEquals(firstPredicate.line, repeatedPredicate.line)
            assertTrue(firstPredicate.stackName != firstHandle.stackName)
            assertTrue(repeatedPredicate.stackName != repeatedHandle.stackName)
        } finally {
            launchedExamples.forEach { it.destroyForcibly() }
            stopNewExampleProcesses(root, existingExamples)
        }
    }

    private fun startExamples(rider: RiderDriver, root: Path): List<Process> {
        if (!System.getProperty("dotboxd.e2e.external-launch", "false").toBoolean()) {
            rider.startRunConfiguration("Start Examples")
            return emptyList()
        }

        val processes = mutableListOf<Process>()
        try {
            processes += startExample(
                root,
                "samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj",
                "GameServer - Wait for Plugin (Debug)",
            )
            processes += startExample(
                root,
                "samples/GameServer/Examples.GameServer.Plugin/Examples.GameServer.Plugin.csproj",
                "GameServer Plugin (Debug)",
            )
            return processes
        } catch (exception: RuntimeException) {
            processes.forEach { it.destroyForcibly() }
            throw exception
        }
    }

    private fun startExample(root: Path, project: String, profile: String): Process =
        ProcessBuilder(
            "dotnet",
            "run",
            "--project",
            project,
            "-c",
            "Debug",
            "--no-build",
            "--launch-profile",
            profile,
        ).directory(root.toFile())
            .redirectOutput(ProcessBuilder.Redirect.DISCARD)
            .redirectError(ProcessBuilder.Redirect.DISCARD)
            .start()

    private fun awaitStop(rider: RiderDriver, predicate: (DebugStop) -> Boolean): DebugStop {
        var result: DebugStop? = null
        var observed: DebugStop? = null
        try {
            waitFor(Duration.ofSeconds(30), Duration.ofMillis(200)) {
                rider.debugStop()?.also { observed = it }?.takeIf(predicate)?.also { result = it } != null
            }
        } catch (exception: RuntimeException) {
            throw AssertionError(
                "Expected kernel stop was not observed. Last stop=$observed; ${rider.debugSummary()}",
                exception,
            )
        }
        return requireNotNull(result)
    }

    private fun awaitPluginBridge(existing: Set<Long>): Long {
        var result: Long? = null
        waitFor(Duration.ofMinutes(3), Duration.ofMillis(250)) {
            liveBridgeProcessIds().firstOrNull { it !in existing }?.also { result = it } != null
        }
        return requireNotNull(result)
    }

    private fun liveBridgeProcessIds(): Set<Long> {
        val localData = requireNotNull(System.getenv("LOCALAPPDATA"))
        val directory = Path.of(localData, "DotBoxD", "Debug")
        if (!Files.isDirectory(directory)) return emptySet()
        return Files.list(directory).use { paths ->
            paths.toList().mapNotNull { path ->
                path.fileName.toString().removeSuffix(".json").toLongOrNull()
            }.filter { processId ->
                ProcessHandle.of(processId).map(ProcessHandle::isAlive).orElse(false)
            }.toList().toSet()
        }
    }

    private fun exampleProcessIds(root: Path): Set<Long> = ProcessHandle.allProcesses().use { processes ->
        processes.filter { isExampleProcess(it, root) }.map(ProcessHandle::pid).toList().toSet()
    }

    private fun stopNewExampleProcesses(root: Path, existing: Set<Long>) {
        repeat(20) {
            val launched = ProcessHandle.allProcesses().use { processes ->
                processes.filter { it.pid() !in existing && isExampleProcess(it, root) }.toList()
            }
            launched.forEach(ProcessHandle::destroyForcibly)
            if (launched.isEmpty()) return
            Thread.sleep(100)
        }
        check(exampleProcessIds(root).all(existing::contains)) { "Rider left GameServer example processes running" }
    }

    private fun isExampleProcess(process: ProcessHandle, root: Path): Boolean {
        val commandLine = process.info().commandLine().orElse("").replace('\\', '/')
        val normalizedRoot = root.toString().replace('\\', '/')
        return commandLine.startsWith(normalizedRoot, ignoreCase = true) &&
            commandLine.contains("Examples.GameServer", ignoreCase = true)
    }
}
