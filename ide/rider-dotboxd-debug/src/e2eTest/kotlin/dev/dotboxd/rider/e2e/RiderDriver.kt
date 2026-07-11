package dev.dotboxd.rider.e2e

import com.intellij.remoterobot.RemoteRobot
import com.intellij.remoterobot.fixtures.ContainerFixture
import com.intellij.remoterobot.search.locators.byXpath
import com.intellij.remoterobot.utils.waitFor
import java.time.Duration

internal class RiderDriver(private val remoteRobot: RemoteRobot) {
    private val frame = remoteRobot.find<ContainerFixture>(
        byXpath("//div[@class='IdeFrameImpl']"),
        timeout = Duration.ofMinutes(5),
    )

    fun awaitReady() {
        waitFor(Duration.ofMinutes(5), Duration.ofSeconds(2)) {
            call<Boolean>(
                """
                const project = projectOf(component);
                project != null;
                """,
            )
        }
    }

    fun projectName(): String = call("projectOf(component).getName();")

    fun pluginLoaded(): Boolean = call(
        "com.intellij.ide.plugins.PluginManagerCore.getPlugin(" +
            "com.intellij.openapi.extensions.PluginId.getId('dev.dotboxd.kernel-debug')) != null;",
    )

    fun startRunConfiguration(name: String) {
        run(
            """
            const project = projectOf(component);
            const settings = com.intellij.execution.RunManager.getInstance(project)
                .findConfigurationByName('${js(name)}');
            if (!settings) throw 'Run configuration not found: ${js(name)}';
            const action = new java.lang.Runnable({ run: function() {
                com.intellij.execution.ProgramRunnerUtil.executeConfiguration(
                    settings,
                    com.intellij.execution.executors.DefaultRunExecutor.getRunExecutorInstance()
                );
            }});
            com.intellij.openapi.application.ApplicationManager.getApplication().invokeAndWait(action);
            """,
        )
    }

    fun addDotNetBreakpoint(path: String, line: Int) {
        require(line > 0)
        runOffEdt(
            """
            const project = projectOf(component);
            const path = '${js(path.replace('\\', '/'))}';
            const file = com.intellij.openapi.vfs.LocalFileSystem.getInstance().findFileByPath(path);
            if (!file) throw 'Breakpoint file not found: ' + path;
            const types = com.intellij.xdebugger.XDebuggerUtil.getInstance().getLineBreakpointTypes();
            let type = null;
            for (let i = 0; i < types.length; i++) {
                if (String(types[i].getId()) === 'DotNet Breakpoints') type = types[i];
            }
            if (!type) throw 'Rider .NET line breakpoint type is unavailable';
            const manager = com.intellij.xdebugger.XDebuggerManager.getInstance(project).getBreakpointManager();
            const properties = type.createBreakpointProperties(file, ${line - 1});
            manager.addLineBreakpoint(type, file.getUrl(), ${line - 1}, properties);
            """,
        )
    }

    fun clearDotNetBreakpoints() {
        run(
            """
            const project = projectOf(component);
            const action = new java.lang.Runnable({ run: function() {
                const manager = com.intellij.xdebugger.XDebuggerManager.getInstance(project).getBreakpointManager();
                const breakpoints = manager.getAllBreakpoints();
                for (let i = breakpoints.length - 1; i >= 0; i--) {
                    if (String(breakpoints[i].getType().getId()) === 'DotNet Breakpoints') {
                        manager.removeBreakpoint(breakpoints[i]);
                    }
                }
            }});
            com.intellij.openapi.application.ApplicationManager.getApplication().invokeAndWait(action);
            """,
        )
    }

    fun attachToKernels(processId: Long, runRegisteredRunnerDirectly: Boolean) {
        run(
            """
            const project = projectOf(component);
            const types = com.intellij.execution.configurations.ConfigurationType.CONFIGURATION_TYPE_EP
                .getExtensionList();
            let type = null;
            for (let i = 0; i < types.size(); i++) {
                if (String(types.get(i).getId()) === 'DotBoxDKernelDebugConfiguration') type = types.get(i);
            }
            if (!type) throw 'DotBoxD run configuration type is unavailable';
            const settings = com.intellij.execution.RunManager.getInstance(project).createConfiguration(
                'DotBoxD E2E Attach',
                type.getConfigurationFactories()[0]
            );
            settings.getConfiguration().setProcessId($processId);
            settings.getConfiguration().setPauseScope('Execution');
            const executor = com.intellij.execution.executors.DefaultDebugExecutor.getDebugExecutorInstance();
            if ($runRegisteredRunnerDirectly) {
                const configuration = settings.getConfiguration();
                const runner = com.intellij.execution.runners.ProgramRunner.getRunner(
                    executor.getId(),
                    configuration
                );
                if (!runner) throw 'DotBoxD debug program runner is unavailable';
                const environment = com.intellij.execution.runners.ExecutionEnvironmentBuilder
                    .create(project, executor, configuration)
                    .runner(runner)
                    .build();
                runner.execute(environment);
            } else {
                com.intellij.execution.ProgramRunnerUtil.executeConfiguration(settings, executor);
            }
            """,
        )
    }

    fun debugStop(): DebugStop? {
        val value = call<String>(
            """
            const project = projectOf(component);
            const session = com.intellij.xdebugger.XDebuggerManager.getInstance(project).getCurrentSession();
            if (!session || !session.isSuspended()) {
                '';
            } else {
                const context = session.getSuspendContext();
                const stack = context ? context.getActiveExecutionStack() : null;
                const top = stack ? stack.getTopFrame() : null;
                const position = top ? top.getSourcePosition() : null;
                const path = position && position.getFile() ? position.getFile().getPath() : '';
                const line = position ? position.getLine() + 1 : 0;
                const stackName = stack ? stack.getDisplayName() : '';
                path + '|' + line + '|' + stackName;
            }
            """,
        )
        if (value.isBlank()) return null
        val parts = value.split('|', limit = 3)
        return DebugStop(parts[0], parts[1].toInt(), parts[2])
    }

    fun debugSummary(): String = call(
        """
        const project = projectOf(component);
        const manager = com.intellij.xdebugger.XDebuggerManager.getInstance(project);
        const sessions = manager.getDebugSessions();
        let result = 'current=' + (manager.getCurrentSession() ? manager.getCurrentSession().getSessionName() : 'none');
        for (let i = 0; i < sessions.length; i++) {
            const context = sessions[i].getSuspendContext();
            const stack = context ? context.getActiveExecutionStack() : null;
            const top = stack ? stack.getTopFrame() : null;
            const position = top ? top.getSourcePosition() : null;
            result += '; session=' + sessions[i].getSessionName() +
                ', suspended=' + sessions[i].isSuspended() +
                ', stack=' + (stack ? stack.getDisplayName() : 'none') +
                ', path=' + (position && position.getFile() ? position.getFile().getPath() : 'none') +
                ', line=' + (position ? position.getLine() + 1 : 0);
        }
        result;
        """,
    )

    fun resume() {
        run(
            """
            const project = projectOf(component);
            const session = com.intellij.xdebugger.XDebuggerManager.getInstance(project).getCurrentSession();
            if (!session || !session.isSuspended()) throw 'No suspended DotBoxD debug session';
            session.resume();
            """,
        )
    }

    private inline fun <reified T> call(script: String): T = frame.callJs(prologue + script.trimIndent(), true)

    private fun run(script: String) = frame.runJs(prologue + script.trimIndent(), true)

    private fun runOffEdt(script: String) = frame.runJs(prologue + script.trimIndent(), false)

    private fun js(value: String): String = value.replace("\\", "\\\\").replace("'", "\\'")

    private companion object {
        val prologue =
            """
            function projectOf(frame) {
                return com.intellij.openapi.wm.impl.ProjectFrameHelper.getFrameHelper(frame).getProject();
            }
            """.trimIndent() + "\n"
    }
}

internal data class DebugStop(val path: String, val line: Int, val stackName: String)
