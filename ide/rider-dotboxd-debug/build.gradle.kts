import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import org.jetbrains.kotlin.gradle.tasks.KotlinCompile

plugins {
    kotlin("jvm") version "2.1.0"
    id("org.jetbrains.intellij.platform") version "2.18.1"
}

group = "dev.dotboxd"
version = providers.gradleProperty("pluginVersion").getOrElse("0.1.0-SNAPSHOT")

repositories {
    mavenCentral()
    intellijPlatform { defaultRepositories() }
}

dependencies {
    intellijPlatform {
        rider("2025.2.1") { useInstaller.set(false) }
        jetbrainsRuntime()
        zipSigner()
    }
    testImplementation(kotlin("test"))
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
    inputs.file(adapterProject)
    inputs.files(fileTree("../../tools/DotBoxD.DebugAdapter") { include("**/*.cs") })
    outputs.dir(adapterOutput)
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
