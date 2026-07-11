package dev.dotboxd.rider.run

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class DotBoxDRunConfigurationTest {
    @Test
    fun `pause scopes use protocol casing`() {
        assertEquals("server", dapPauseScope("Server"))
        assertEquals("pluginSession", dapPauseScope("PluginSession"))
        assertEquals("execution", dapPauseScope("Execution"))
    }

    @Test
    fun `host default pause scope is omitted`() {
        assertNull(dapPauseScope(DotBoxDRunConfiguration.HOST_DEFAULT_PAUSE_SCOPE))
    }
}
