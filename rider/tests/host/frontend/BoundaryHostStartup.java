package anotherfsm.acceptance;

import com.intellij.openapi.project.Project;
import com.intellij.openapi.startup.StartupActivity;
import java.nio.file.Path;

/** Compiled/registered only in the separately prepared boundary test observer, never the product ZIP. */
public final class BoundaryHostStartup implements StartupActivity.DumbAware {
  public void runActivity(Project project) {
    if(!Boolean.getBoolean("anotherfsm.acceptance")||!Boolean.getBoolean("anotherfsm.boundaryAcceptance")||Boolean.getBoolean("anotherfsm.sourceBoundaryAcceptance"))return;
    ComplexBoundaryAcceptance.start(project,Path.of(System.getProperty("anotherfsm.acceptance.dir")),
        Path.of(System.getProperty("anotherfsm.fixture.root")),() -> {});
  }
}
