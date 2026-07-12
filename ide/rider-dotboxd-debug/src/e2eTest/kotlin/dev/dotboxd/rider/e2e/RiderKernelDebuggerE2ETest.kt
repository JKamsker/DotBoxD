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
        var launchedExamples: ExternalExamples? = null
        try {
            rider.clearDotNetBreakpoints()
            rider.addDotNetBreakpoint(guardian.toString(), 35)
            rider.addDotNetBreakpoint(guardian.toString(), 44)
            launchedExamples = startExamples(rider, root)
            val pluginProcessId = awaitPluginBridge(existingBridges, root)
            // Hosted Rider has no interactive license session, so its product-level launch helper is suppressed.
            // Invoking the registered runner directly still exercises the real adapter and XDebugger integration.
            rider.attachToKernels(
                pluginProcessId,
                runRegisteredRunnerDirectly = System.getProperty("dotboxd.e2e.external-launch", "false").toBoolean(),
            )
            val adapterProcessId = awaitAdapter(rider)
            launchedExamples?.releaseAfterBreakpointsReady()

            val firstPredicate = awaitStop(rider, launchedExamples) {
                it.path.endsWith("GuardianKernel.cs") && it.line == 35
            }
            assertKernelIdeState(rider, firstPredicate, guardian, 35)
            rider.stepOver()
            val step = awaitStop(rider, launchedExamples) {
                it.path.endsWith("GuardianKernel.cs") &&
                    it.stackName == firstPredicate.stackName && it.line != firstPredicate.line
            }
            rider.resume()
            val firstHandle = awaitStop(rider, launchedExamples) {
                it.path.endsWith("GuardianKernel.cs") && it.line == 44
            }
            rider.resume()
            val repeatedPredicate = awaitStop(rider, launchedExamples) {
                it.path.endsWith("GuardianKernel.cs") &&
                    it.line == 35 && it.stackName != firstPredicate.stackName
            }
            rider.resume()
            val repeatedHandle = awaitStop(rider, launchedExamples) {
                it.path.endsWith("GuardianKernel.cs") && it.line == 44
            }
            assertKernelIdeState(rider, repeatedHandle, guardian, 44)

            assertEquals(firstPredicate.line, repeatedPredicate.line)
            assertTrue(firstPredicate.stackName != firstHandle.stackName)
            assertTrue(repeatedPredicate.stackName != repeatedHandle.stackName)
            assertTrue(step.line > 0)

            rider.stopDebugSession()
            waitFor(Duration.ofSeconds(30), Duration.ofMillis(200)) { !rider.hasDebugSession() }
            waitFor(Duration.ofSeconds(30), Duration.ofMillis(200)) {
                ProcessHandle.of(adapterProcessId).map(ProcessHandle::isAlive).orElse(false).not()
            }
            val transcript = adapterLog(root)
            waitFor(Duration.ofSeconds(30), Duration.ofMillis(200)) {
                Files.isRegularFile(transcript) && Files.readString(transcript).contains(" adapter completed disconnect")
            }
            AdapterTranscript.assertComplete(transcript)
        } finally {
            launchedExamples?.destroy()
            stopNewExampleProcesses(root, existingExamples)
        }
    }

    private fun assertKernelIdeState(rider: RiderDriver, stop: DebugStop, guardian: Path, line: Int) {
        assertEquals(guardian.normalize().toString().replace('\\', '/'), stop.path.replace('\\', '/'))
        assertEquals(line, stop.line)
        val guardianPath = guardian.toString().replace('\\', '/')
        val breakpoints = rider.dotNetBreakpoints().filter { it.path.replace('\\', '/') == guardianPath }
        assertEquals(2, breakpoints.size)
        assertEquals(listOf(35, 44), breakpoints.filter(IdeBreakpoint::enabled).map(IdeBreakpoint::line).sorted())
    }

    private fun adapterLog(root: Path): Path = root.resolve(
        "ide/rider-dotboxd-debug/.intellijPlatform/sandbox/dotboxd-kernel-debug-rider/" +
            "RD-2025.2.1/log_runIdeForUiTests/dotboxd-kernel-debug-adapter.log",
    )

    private fun startExamples(rider: RiderDriver, root: Path): ExternalExamples? {
        if (!System.getProperty("dotboxd.e2e.external-launch", "false").toBoolean()) {
            rider.startRunConfiguration("Start Examples")
            return null
        }

        return ExternalExamples.start(root)
    }

    private fun awaitStop(
        rider: RiderDriver,
        examples: ExternalExamples?,
        predicate: (DebugStop) -> Boolean,
    ): DebugStop {
        var result: DebugStop? = null
        var observed: DebugStop? = null
        try {
            waitFor(Duration.ofSeconds(30), Duration.ofMillis(200)) {
                rider.debugStop()?.also { observed = it }?.takeIf(predicate)?.also { result = it } != null
            }
        } catch (exception: RuntimeException) {
            throw AssertionError(
                buildString {
                    append("Expected kernel stop was not observed. Last stop=$observed; ${rider.debugSummary()}")
                    examples?.let { append("\n${it.diagnostics()}") }
                },
                exception,
            )
        }
        return requireNotNull(result)
    }

    private fun awaitPluginBridge(existing: Set<Long>, root: Path): Long {
        var result: Long? = null
        waitFor(Duration.ofMinutes(3), Duration.ofMillis(250)) {
            liveBridgeProcessIds().firstOrNull { processId ->
                processId !in existing && ProcessHandle.of(processId)
                    .filter { isPluginProcess(it, root) }
                    .isPresent
            }?.also { result = it } != null
        }
        return requireNotNull(result)
    }

    private fun awaitAdapter(rider: RiderDriver): Long {
        var result: Long? = null
        waitFor(Duration.ofSeconds(30), Duration.ofMillis(200)) {
            rider.adapterProcessId()?.also { result = it } != null
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
        val command = process.info().command().orElse("").replace('\\', '/')
        val normalizedRoot = root.toString().replace('\\', '/')
        return command.contains(normalizedRoot, ignoreCase = true) &&
            command.contains("Examples.GameServer", ignoreCase = true)
    }

    private fun isPluginProcess(process: ProcessHandle, root: Path): Boolean {
        val command = process.info().command().orElse("").replace('\\', '/')
        val normalizedRoot = root.toString().replace('\\', '/')
        val executable = Path.of(command).fileName?.toString()?.removeSuffix(".exe").orEmpty()
        return command.contains(normalizedRoot, ignoreCase = true) &&
            executable.equals("Examples.GameServer.Plugin", ignoreCase = true)
    }
}
