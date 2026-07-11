package dev.dotboxd.rider.e2e

import com.intellij.remoterobot.utils.waitFor
import java.nio.file.Files
import java.nio.file.Path
import java.time.Duration

internal class ExternalExamples private constructor(
    private val processes: List<ExampleProcess>,
) {
    fun destroy() = processes.asReversed().forEach { it.process.destroyForcibly() }

    fun diagnostics(): String = processes.joinToString(separator = "\n") { example ->
        buildString {
            append(example.name)
            append(" process ")
            append(if (example.process.isAlive) "is still running" else "exited with code ${example.process.exitValue()}")
            append("\nstdout tail:\n")
            append(readTail(example.stdout))
            append("\nstderr tail:\n")
            append(readTail(example.stderr))
        }
    }

    private data class ExampleProcess(
        val name: String,
        val process: Process,
        val stdout: Path,
        val stderr: Path,
    )

    companion object {
        private val readinessTimeout = Duration.ofMinutes(3)
        private const val pipeName = "dotboxd-game-debug-0123456789abcdef"

        fun start(root: Path): ExternalExamples {
            val artifactDirectory = Files.createDirectories(root.resolve("artifacts/rider-e2e"))
            val launched = mutableListOf<ExampleProcess>()
            try {
                val server = startExample(
                    root,
                    artifactDirectory,
                    "server",
                    "samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj",
                    "GameServer - Wait for Plugin (Debug)",
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
                awaitOutput(plugin, "[plugin] kernel debug bridge ready for PID ")
                return ExternalExamples(launched)
            } catch (exception: Exception) {
                val examples = ExternalExamples(launched)
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
                "${example.name} did not report readiness.\n${ExternalExamples(listOf(example)).diagnostics()}"
            }
        }

        private fun readLog(path: Path): String = if (Files.isRegularFile(path)) Files.readString(path) else ""

        private fun readTail(path: Path): String {
            val content = readLog(path)
            return if (content.length <= 8_192) content else content.takeLast(8_192)
        }
    }
}
