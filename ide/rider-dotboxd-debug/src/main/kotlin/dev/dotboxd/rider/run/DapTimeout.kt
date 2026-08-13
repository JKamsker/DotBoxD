package dev.dotboxd.rider.run

import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit

private const val DAP_REQUEST_TIMEOUT_SECONDS = 10L

internal fun <T> CompletableFuture<T>.awaitDap(): T =
    get(DAP_REQUEST_TIMEOUT_SECONDS, TimeUnit.SECONDS)
