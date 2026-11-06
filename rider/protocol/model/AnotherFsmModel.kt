package model.anotherfsm

import com.jetbrains.rd.generator.nova.*
import com.jetbrains.rd.generator.nova.PredefinedType.string
import com.jetbrains.rd.generator.nova.csharp.CSharp50Generator
import com.jetbrains.rd.generator.nova.kotlin.Kotlin11Generator
import com.jetbrains.rider.model.nova.ide.SolutionModel

/** Transport only. JSON envelopes and semantic/version validation belong to the adapters. */
@Suppress("unused")
object AnotherFsmModel : Ext(SolutionModel.Solution) {
    init {
        setting(Kotlin11Generator.Namespace, "com.anotherfsm.rider.protocol")
        setting(CSharp50Generator.Namespace, "AnotherFSM.Rider.Protocol")

        call("query", string, string).async
        signal("invalidated", string).async
    }
}
