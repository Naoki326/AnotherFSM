package com.anotherfsm.rider.protocol.examples;

import com.anotherfsm.rider.protocol.AnotherFsmModel;
import com.anotherfsm.rider.protocol.AnotherFsmModel_GeneratedKt;
import com.intellij.openapi.project.Project;
import com.jetbrains.rd.framework.IRdTask;
import com.jetbrains.rd.util.lifetime.Lifetime;
import com.jetbrains.rider.projectView.SolutionHostExtensionsKt;

/** Java consumption seam; same host scheduler/lifetime preconditions as FrontendBinding.kt. */
public final class FrontendJavaBinding {
    private FrontendJavaBinding() {}

    public static AnotherFsmModel acquire(Project project) {
        return AnotherFsmModel_GeneratedKt.getAnotherFsmModel(SolutionHostExtensionsKt.getSolution(project));
    }

    public static IRdTask<String> query(AnotherFsmModel model, Lifetime requestLifetime, String envelope) {
        return model.getQuery().start(requestLifetime, envelope, null);
    }
}
