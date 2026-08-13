package dev.dotboxd.rider.run

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class StoppedExecutionStateTest {
    @Test
    fun `stopped execution can only be claimed once`() {
        val state = StoppedExecutionState()
        state.stopped(7)

        assertEquals(7, state.claim())
        assertNull(state.claim())
        assertNull(state.current())
    }

    @Test
    fun `failed claim does not replace a newer stop`() {
        val state = StoppedExecutionState()
        state.stopped(7)
        assertEquals(7, state.claim())

        state.stopped(8)
        state.restore(7)

        assertEquals(8, state.current())
    }

    @Test
    fun `failed claim is restored when no newer stop exists`() {
        val state = StoppedExecutionState()
        state.stopped(7)

        state.restore(requireNotNull(state.claim()))

        assertEquals(7, state.current())
    }
}
