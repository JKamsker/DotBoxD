package dev.dotboxd.rider.run

import java.util.concurrent.atomic.AtomicInteger

internal class StoppedExecutionState {
    private val threadId = AtomicInteger(NO_THREAD)

    fun stopped(newThreadId: Int) {
        require(newThreadId > 0) { "A stopped DAP thread ID must be positive." }
        threadId.set(newThreadId)
    }

    fun current(): Int? = threadId.get().takeIf { it > 0 }

    fun claim(): Int? = threadId.getAndSet(NO_THREAD).takeIf { it > 0 }

    fun restore(claimedThreadId: Int) {
        require(claimedThreadId > 0) { "A claimed DAP thread ID must be positive." }
        threadId.compareAndSet(NO_THREAD, claimedThreadId)
    }

    fun clear() {
        threadId.set(NO_THREAD)
    }

    private companion object {
        const val NO_THREAD = -1
    }
}
