package dev.dotboxd.rider.e2e

import com.intellij.remoterobot.utils.waitFor
import java.nio.file.Files
import java.nio.file.Path
import java.time.Duration

internal class ExternalExamples private constructor(
    private val processes: List<ExampleProcess>,
    private val startGate: Path,
    private val ideaLog: Path,
    private val existingBreakpointSynchronizations: Int,
) {
    fun releaseAfterBreakpointsReady() {
        waitFor(readinessTimeout, Duration.ofMillis(100)) {
            processes.any { !it.process.isAlive } ||
                occurrences(readLog(ideaLog), breakpointSyncMarker) > existingBreakpointSynchronizations
        }
        check(
            processes.all { it.process.isAlive } &&
                occurrences(readLog(ideaLog), breakpointSyncMarker) > existingBreakpointSynchronizations,
        ) {
            "Rider did not synchronize both kernel breakpoints.\n${diagnostics()}"
        }
        Files.writeString(startGate, "ready")
    }

    fun destroy() {
        processes.asReversed().forEach { it.process.destroyForcibly() }
        Files.deleteIfExists(startGate)
    }

    fun diagnostics(): String = diagnostics(processes)

    private data class ExampleProcess(
        val name: String,
        val process: Process,
        val stdout: Path,
        val stderr: Path,
    )

    companion object {
        private val readinessTimeout = Duration.ofMinutes(3)
        private const val pipeName = "dotboxd-game-debug-0123456789abcdef"
        private const val breakpointSyncMarker = "35=verified, 44=verified"
        private const val startGateVariable = "DOTBOXD_E2E_CONTINUOUS_START_GATE"

        fun start(root: Path): ExternalExamples {
            val artifactDirectory = Files.createDirectories(root.resolve("artifacts/rider-e2e"))
            val startGate = artifactDirectory.resolve("start-debug-rounds.signal")
            val ideaLog = root.resolve(
                "ide/rider-dotboxd-debug/.intellijPlatform/sandbox/dotboxd-kernel-debug-rider/" +
                    "RD-2025.2.1/log_runIdeForUiTests/idea.log",
            )
            Files.deleteIfExists(startGate)
            val launched = mutableListOf<ExampleProcess>()
            try {
                val server = startExample(
                    root,
                    artifactDirectory,
                    "server",
                    "samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj",
                    "GameServer - Wait for Plugin (Debug)",
                    mapOf(startGateVariable to startGate.toString()),
                )
                launched += server
                awaitOutput(server, "[server] listening for plugin on pipe '$pipeName'.")

                val plugin = startExample(
                    root,
                    artifactDirectory,
                    "plugin",
                    "samples/GameServer/Examples.GameServer.Plugin/Examples.GameServer.Plugin.csproj",
                    "GameServer Plugin (Debug)",
                    mapOf("DOTBOXD_E2E_SKIP_ADVANCED_USAGE" to "1"),
                )
                launched += plugin
                awaitOutput(plugin, "[plugin] kernels live; holding until server completes...")
                awaitOutput(server, "[server] waiting for Rider E2E breakpoint synchronization...")
                check(launched.all { it.process.isAlive }) { "An example exited before Rider attached." }
                return ExternalExamples(
                    launched,
                    startGate,
                    ideaLog,
                    occurrences(readLog(ideaLog), breakpointSyncMarker),
                )
            } catch (exception: Exception) {
                val examples = ExternalExamples(launched, startGate, ideaLog, 0)
                examples.destroy()
                throw AssertionError("External example startup failed.\n${examples.diagnostics()}", exception)
            }
        }

        private fun startExample(
            root: Path,
            artifactDirectory: Path,
            name: String,
            project: String,
            profile: String,
            environment: Map<String, String> = emptyMap(),
        ): ExampleProcess {
            val stdout = artifactDirectory.resolve("$name.stdout.log")
            val stderr = artifactDirectory.resolve("$name.stderr.log")
            Files.deleteIfExists(stdout)
            Files.deleteIfExists(stderr)
            val builder = ProcessBuilder(
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
                .redirectOutput(stdout.toFile())
                .redirectError(stderr.toFile())
            builder.environment().putAll(environment)
            val process = builder.start()
            return ExampleProcess(name, process, stdout, stderr)
        }

        private fun awaitOutput(example: ExampleProcess, expected: String) {
            waitFor(readinessTimeout, Duration.ofMillis(250)) {
                !example.process.isAlive || readLog(example.stdout).contains(expected, ignoreCase = false)
            }
            check(example.process.isAlive && readLog(example.stdout).contains(expected, ignoreCase = false)) {
                "${example.name} did not report readiness.\n${diagnostics(listOf(example))}"
            }
        }

        private fun readLog(path: Path): String = if (Files.isRegularFile(path)) Files.readString(path) else ""

        private fun occurrences(content: String, value: String): Int {
            var count = 0
            var offset = 0
            while (true) {
                offset = content.indexOf(value, offset)
                if (offset < 0) return count
                count++
                offset += value.length
            }
        }

        private fun readTail(path: Path): String {
            val content = readLog(path)
            return if (content.length <= 8_192) content else content.takeLast(8_192)
        }

        private fun diagnostics(examples: List<ExampleProcess>): String = examples.joinToString(separator = "\n") {
            buildString {
                append(it.name)
                append(" process ")
                append(if (it.process.isAlive) "is still running" else "exited with code ${it.process.exitValue()}")
                append("\nstdout tail:\n")
                append(readTail(it.stdout))
                append("\nstderr tail:\n")
                append(readTail(it.stderr))
            }
        }
    }
}
