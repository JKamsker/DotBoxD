package dev.dotboxd.rider.run

import java.nio.file.Files
import java.nio.file.Path
import kotlin.io.path.writeText
import kotlin.test.Test
import kotlin.test.assertEquals

class DotBoxDDiscoveryTest {
    @Test
    fun `uses each platform's per-user application data directory`() {
        val home = Path.of("home", "developer")

        assertEquals(
            Path.of("local-data", "DotBoxD", "Debug"),
            DotBoxDDiscovery.directory("Windows 11", mapOf("LOCALAPPDATA" to "local-data"), home),
        )
        assertEquals(
            home.resolve("Library/Application Support/DotBoxD/Debug"),
            DotBoxDDiscovery.directory("Mac OS X", emptyMap(), home),
        )
        assertEquals(
            Path.of("xdg-data", "DotBoxD", "Debug"),
            DotBoxDDiscovery.directory("Linux", mapOf("XDG_DATA_HOME" to "xdg-data"), home),
        )
    }

    @Test
    fun `discovers live process ids without reading descriptor contents`() {
        val directory = Files.createTempDirectory("dotboxd-rider-discovery")
        try {
            directory.resolve("42.json").writeText(
                """{"ProcessId":42,"PipeName":"dotboxd-debug-42","DiscoveryToken":"secret-not-loaded"}""",
            )
            directory.resolve("invalid.json").writeText("not-json")
            directory.resolve("ignored.txt").writeText("{}")

            assertEquals(
                listOf(DotBoxDBridgeDescriptor(42)),
                DotBoxDDiscovery.read(directory) { it == 42 },
            )
        } finally {
            directory.toFile().deleteRecursively()
        }
    }
}
