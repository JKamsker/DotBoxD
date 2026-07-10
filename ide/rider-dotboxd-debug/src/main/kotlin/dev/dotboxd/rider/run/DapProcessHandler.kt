package dev.dotboxd.rider.run

import com.intellij.execution.process.ProcessHandler
import java.io.OutputStream

class DapProcessHandler(private val process: Process) : ProcessHandler() {
    override fun destroyProcessImpl() {
        process.destroyForcibly()
    }

    override fun detachProcessImpl() {
        process.destroyForcibly()
    }

    override fun detachIsDefault() = false
    override fun getProcessInput(): OutputStream = process.outputStream

    fun process() = process
    fun terminated(exitCode: Int) = notifyProcessTerminated(exitCode)
}
