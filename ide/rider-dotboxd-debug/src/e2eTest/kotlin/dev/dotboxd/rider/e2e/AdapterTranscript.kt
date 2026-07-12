package dev.dotboxd.rider.e2e

import java.nio.file.Files
import java.nio.file.Path

internal object AdapterTranscript {
    private val requiredCommands = listOf(
        "initialize",
        "attach",
        "setBreakpoints",
        "configurationDone",
        "stackTrace",
        "scopes",
        "variables",
        "completions",
        "next",
        "continue",
        "disconnect",
    )
    private val request = Regex(" adapter request (\\S+)$")
    private val completed = Regex(" adapter completed (\\S+)$")
    private val mappedStop = Regex(" adapter stack \\d+ (ShouldHandle|Handle) line (35|44)$")

    fun assertComplete(path: Path) {
        check(Files.isRegularFile(path)) { "Rider did not create the kernel debug adapter transcript at $path" }
        val lines = Files.readAllLines(path)
        val error = lines.firstOrNull {
            it.contains(Regex(" adapter (?:error|unhandled error) ")) &&
                !it.contains(" adapter adapter error evaluationFailed:") &&
                !it.contains(" adapter adapter error staleVariables:")
        }
        check(error == null) { "The kernel debug adapter logged an error: $error" }

        val requests = lines.mapNotNull { request.find(it)?.groupValues?.get(1) }
        val completions = lines.mapNotNull { completed.find(it)?.groupValues?.get(1) }
        val missing = requiredCommands.filterNot(requests::contains)
        check(missing.isEmpty()) { "The kernel debug adapter transcript is missing requests: $missing" }
        val incomplete = requiredCommands.filterNot(completions::contains)
        check(incomplete.isEmpty()) { "The kernel debug adapter did not complete requests: $incomplete" }
        check(lines.any { it.endsWith(" adapter bridge remote stepOver") }) {
            "Rider Step Over did not reach the remote kernel debugger"
        }
        check(lines.count(mappedStop::containsMatchIn) >= 4) {
            "Expected at least four source-mapped kernel stops"
        }
    }
}
