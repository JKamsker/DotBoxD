import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import org.jetbrains.kotlin.gradle.tasks.KotlinCompile
import org.gradle.process.CommandLineArgumentProvider

plugins {
    kotlin("jvm") version "2.1.0"
    id("org.jetbrains.intellij.platform") version "2.18.1"
}

group = "dev.dotboxd"
version = providers.gradleProperty("pluginVersion").getOrElse("0.1.0-SNAPSHOT")

repositories {
    mavenCentral()
    maven("https://packages.jetbrains.team/maven/p/ij/intellij-dependencies")
    intellijPlatform { defaultRepositories() }
}

val remoteRobotVersion = "0.11.23"
val e2eTest by sourceSets.creating

dependencies {
    intellijPlatform {
        rider("2025.2.1") { useInstaller.set(false) }
        jetbrainsRuntime()
        zipSigner()
    }
    testImplementation(kotlin("test"))
    add(e2eTest.implementationConfigurationName, kotlin("test"))
    add(e2eTest.implementationConfigurationName, "org.junit.jupiter:junit-jupiter-api:5.11.4")
    add(e2eTest.implementationConfigurationName, "com.intellij.remoterobot:remote-robot:$remoteRobotVersion")
    add(e2eTest.implementationConfigurationName, "com.intellij.remoterobot:remote-fixtures:$remoteRobotVersion")
    add(e2eTest.implementationConfigurationName, "com.squareup.okhttp3:okhttp:4.12.0")
    add(e2eTest.runtimeOnlyConfigurationName, "org.junit.jupiter:junit-jupiter-engine:5.11.4")
    add(e2eTest.runtimeOnlyConfigurationName, "org.junit.platform:junit-platform-launcher:1.11.4")
}

kotlin { jvmToolchain(21) }

tasks.withType<KotlinCompile>().configureEach {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_21)
        freeCompilerArgs.add("-Xjvm-default=all")
    }
}

val adapterProject = layout.projectDirectory.file("../../tools/DotBoxD.DebugAdapter/DotBoxD.DebugAdapter.csproj")
val adapterOutput = layout.buildDirectory.dir("dotboxd-debug-adapter")
val publishAdapter by tasks.registering(Exec::class) {
    outputs.dir(adapterOutput)
    outputs.upToDateWhen { false }
    commandLine(
        "dotnet", "publish", adapterProject.asFile.absolutePath,
        "-c", "Release", "--nologo", "-o", adapterOutput.get().asFile.absolutePath,
    )
}

tasks.processResources {
    dependsOn(publishAdapter)
    from(adapterOutput) {
        include("**/*.dll", "**/*.json")
        into("adapter")
    }
}

tasks.test { useJUnitPlatform() }

tasks.register<Test>("e2eTest") {
    description = "Runs end-to-end tests against the Rider UI test sandbox."
    group = "verification"
    testClassesDirs = e2eTest.output.classesDirs
    classpath = e2eTest.runtimeClasspath
    useJUnitPlatform()
    jvmArgs("--add-opens=java.base/java.lang=ALL-UNNAMED")
    testLogging.showStandardStreams = true
    outputs.upToDateWhen { false }
    shouldRunAfter(tasks.test)
    systemProperty("remote-robot-url", providers.gradleProperty("remoteRobotUrl").getOrElse("http://127.0.0.1:8082"))
    systemProperty("dotboxd.e2e.root", layout.projectDirectory.dir("../..").asFile.absolutePath)
    systemProperty(
        "dotboxd.e2e.external-launch",
        providers.gradleProperty("dotboxdE2eExternalLaunch").getOrElse("false"),
    )
}

intellijPlatformTesting.runIde {
    register("runIdeForUiTests") {
        task {
            args(layout.projectDirectory.file("../../DotBoxD.slnx").asFile.absolutePath)
            jvmArgumentProviders.add(CommandLineArgumentProvider {
                listOf(
                    "-Drobot-server.port=8082",
                    "-Djb.privacy.policy.text=<!--999.999-->",
                    "-Djb.consents.confirmation.enabled=false",
                    "-Didea.trust.all.projects=true",
                    "-Dide.show.tips.on.startup.default.value=false",
                    "-Dide.mac.message.dialogs.as.sheets=false",
                    "-Dide.mac.file.chooser.native=false",
                )
            })
        }
        plugins {
            robotServerPlugin(remoteRobotVersion)
        }
    }
}

intellijPlatform {
    pluginConfiguration {
        id = "dev.dotboxd.kernel-debug"
        name = "DotBoxD Kernel Debugger"
        version = project.version.toString()
        description = "Debug server-executed DotBoxD kernels from JetBrains Rider."
        vendor { name = "DotBoxD" }
        ideaVersion {
            sinceBuild = "252"
        }
    }
}
