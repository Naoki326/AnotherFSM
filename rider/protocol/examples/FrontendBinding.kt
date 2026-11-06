package com.anotherfsm.rider.protocol.examples

import com.anotherfsm.rider.protocol.AnotherFsmModel
import com.anotherfsm.rider.protocol.anotherFsmModel
import com.intellij.openapi.project.Project
import com.jetbrains.rd.framework.IRdTask
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rider.projectView.solution

/** Compile-checked integration seam, not a registered plugin service.
 * Acquire on the host protocol scheduler, after the host Solution exists.
 * Never cache across Solution replacement/reconnect.
 */
object FrontendBinding {
    fun acquire(project: Project): AnotherFsmModel = project.solution.anotherFsmModel

    fun query(model: AnotherFsmModel, requestLifetime: Lifetime, envelope: String): IRdTask<String> =
        model.query.start(requestLifetime, envelope)

    fun adviseInvalidated(model: AnotherFsmModel, solutionLifetime: Lifetime, listener: (String) -> Unit) {
        model.invalidated.advise(solutionLifetime, listener)
    }
}
