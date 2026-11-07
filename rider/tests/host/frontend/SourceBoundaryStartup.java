package anotherfsm.acceptance;

import com.intellij.openapi.project.Project;
import com.intellij.openapi.startup.StartupActivity;
import java.nio.file.Path;

/** Explicit source-policy suite in the owned Boundary solution; never registered in the product. */
public final class SourceBoundaryStartup implements StartupActivity.DumbAware {
  public void runActivity(Project project) {
    if(!Boolean.getBoolean("anotherfsm.acceptance")||!Boolean.getBoolean("anotherfsm.sourceBoundaryAcceptance"))return;
    SourceBoundaryAcceptance.start(project,Path.of(System.getProperty("anotherfsm.acceptance.dir")),
        Path.of(System.getProperty("anotherfsm.fixture.root")),() -> {});
  }
}
