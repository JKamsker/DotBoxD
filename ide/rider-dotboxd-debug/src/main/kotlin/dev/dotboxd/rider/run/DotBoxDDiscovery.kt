package dev.dotboxd.rider.run

import java.nio.file.Files
import java.nio.file.Path

data class DotBoxDBridgeDescriptor(val processId: Int)

object DotBoxDDiscovery {
    fun directory(
        osName: String = System.getProperty("os.name"),
        environment: Map<String, String> = System.getenv(),
        home: Path = Path.of(System.getProperty("user.home")),
    ): Path {
        val applicationData = when {
            osName.startsWith("Windows", ignoreCase = true) ->
                environment["LOCALAPPDATA"]?.let(Path::of) ?: home.resolve("AppData/Local")
            osName.startsWith("Mac", ignoreCase = true) -> home.resolve("Library/Application Support")
            else -> environment["XDG_DATA_HOME"]?.let(Path::of) ?: home.resolve(".local/share")
        }
        return applicationData.resolve("DotBoxD/Debug")
    }

    fun read(
        directory: Path = directory(),
        isProcessAlive: (Int) -> Boolean = ::isProcessAlive,
    ): List<DotBoxDBridgeDescriptor> {
        if (!Files.isDirectory(directory)) return emptyList()
        return Files.list(directory).use { paths ->
            paths.iterator().asSequence()
                .filter { it.fileName.toString().endsWith(".json") }
                .mapNotNull { it.fileName.toString().removeSuffix(".json").toIntOrNull() }
                .filter { it > 0 && isProcessAlive(it) }
                .map(::DotBoxDBridgeDescriptor)
                .sortedBy { it.processId }
                .toList()
        }
    }

    private fun isProcessAlive(processId: Int) =
        ProcessHandle.of(processId.toLong()).map(ProcessHandle::isAlive).orElse(false)
}
