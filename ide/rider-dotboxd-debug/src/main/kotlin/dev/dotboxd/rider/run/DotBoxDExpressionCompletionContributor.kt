package dev.dotboxd.rider.run

import com.intellij.codeInsight.completion.CompletionContributor
import com.intellij.codeInsight.completion.CompletionParameters
import com.intellij.codeInsight.completion.CompletionProvider
import com.intellij.codeInsight.completion.CompletionResultSet
import com.intellij.codeInsight.completion.CompletionType
import com.intellij.codeInsight.lookup.LookupElementBuilder
import com.intellij.patterns.PlatformPatterns
import com.intellij.util.ProcessingContext
import com.intellij.xdebugger.XDebuggerManager

class DotBoxDExpressionCompletionContributor : CompletionContributor() {
    init {
        extend(
            CompletionType.BASIC,
            PlatformPatterns.psiElement(),
            object : CompletionProvider<CompletionParameters>() {
                override fun addCompletions(
                    parameters: CompletionParameters,
                    context: ProcessingContext,
                    result: CompletionResultSet,
                ) {
                    val process = XDebuggerManager.getInstance(parameters.position.project)
                        .currentSession?.debugProcess as? DotBoxDDebugProcess ?: return
                    val expression = parameters.editor.document.text.substring(0, parameters.offset)
                    val token = expression.takeLastWhile { it.isLetterOrDigit() || it == '_' || it == '.' }
                    val separator = token.lastIndexOf('.')
                    val parent = if (separator < 0) "" else token.substring(0, separator + 1)
                    val typed = if (separator < 0) token else token.substring(separator + 1)
                    val matches = result.withPrefixMatcher(typed)
                    process.completions(parent).forEach { matches.addElement(LookupElementBuilder.create(it)) }
                }
            },
        )
    }
}
