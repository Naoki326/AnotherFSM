package anotherfsm.acceptance;

import com.google.gson.Gson;
import com.google.gson.JsonParser;
import com.intellij.ide.plugins.PluginManagerCore;
import com.intellij.openapi.extensions.PluginId;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.application.ModalityState;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.startup.StartupActivity;
import com.intellij.util.concurrency.AppExecutorUtil;
import java.awt.Component;
import java.awt.Container;
import java.awt.Window;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.function.BooleanSupplier;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.swing.AbstractButton;
import javax.swing.JDialog;
import javax.swing.JLabel;
import javax.swing.text.JTextComponent;

/** Independent owned-sandbox liveness and cooperative exit. Never runs a test or search. */
public final class AcceptanceLifecycle implements StartupActivity.DumbAware {
  private static final AtomicBoolean active = new AtomicBoolean();
  private record Cancellation(Project project, Runnable cancel, BooleanSupplier clean) {}
  private static final List<Cancellation> cancellations = new CopyOnWriteArrayList<>();

  /** Test-owned participants restore their buffers before the supervisor may close the IDE. */
  public static void onCancellation(Project project, Runnable cancel, BooleanSupplier clean) {
    if (System.getProperty("anotherfsm.acceptance.runId") != null)
      cancellations.add(new Cancellation(project, cancel, clean));
  }

  public void runActivity(Project project) {
    try {
      String runId = System.getProperty("anotherfsm.acceptance.runId", "");
      String suite = System.getProperty("anotherfsm.acceptance.suite", "");
      if (!Boolean.getBoolean("anotherfsm.acceptance") || !UUID.fromString(runId).toString().equals(runId)
          || !runId.equals(System.getenv("ANOTHERFSM_ACCEPTANCE_RUN_ID"))) return;
      String relative = switch (suite) {
        case "legacy" -> "rider/fixtures/Acceptance.sln";
        case "complex" -> "rider/fixtures/Complex/Complex.sln";
        case "boundary", "source" -> "rider/tests/fixtures/boundary/Boundary.sln";
        default -> null;
      };
      if (relative == null) return;
      if (Boolean.getBoolean("anotherfsm.boundaryAcceptance") != List.of("boundary", "source").contains(suite)
          || Boolean.getBoolean("anotherfsm.sourceBoundaryAcceptance") != suite.equals("source")
          || Boolean.getBoolean("anotherfsm.complexAcceptance") != suite.equals("complex")) return;
      Path plugins = Path.of(System.getProperty("idea.plugins.path")).toRealPath();
      Path box = plugins.getParent();
      if (!box.getFileName().toString().equals(".sandbox") || !box.getParent().getFileName().toString().equals("rider")
          || !Files.readString(box.resolve(".anotherfsm-tools-owner")).replace("\r\n", "\n").equals("anotherfsm-issue-32-tools-v1\n")) return;
      Path repository = box.getParent().getParent();
      Path fixture = repository.resolve(relative).toRealPath();
      String suppliedFixture = System.getProperty("anotherfsm.acceptance.fixture");
      if (!fixture.equals(Path.of(suppliedFixture).toRealPath())
          || !fixture.getParent().equals(Path.of(System.getProperty("anotherfsm.fixture.root")).toRealPath())
          || project.getBasePath() == null || !fixture.getParent().equals(Path.of(project.getBasePath()).toRealPath())
          || !box.resolve("config").toRealPath().equals(Path.of(System.getProperty("idea.config.path")).toRealPath())
          || !box.resolve("system").toRealPath().equals(Path.of(System.getProperty("idea.system.path")).toRealPath())) return;
      // Rider's plugin loader legitimately supplies a null ProtectionDomain CodeSource.
      // Match the public descriptor's actual loader and installed plugin path instead.
      var descriptor = PluginManagerCore.getPlugin(PluginId.getId("io.github.naoki326.anotherfsm.acceptance"));
      if (descriptor == null || descriptor.getPluginClassLoader() != AcceptanceLifecycle.class.getClassLoader()
          || !plugins.resolve("anotherfsm-acceptance").toRealPath().equals(descriptor.getPluginPath().toRealPath()))
        throw new IllegalStateException("Lifecycle plugin descriptor/loader/path mismatch");
      Path output = Path.of(System.getProperty("anotherfsm.acceptance.dir"));
      if (!output.isAbsolute() || !Files.isDirectory(output) || !output.getFileName().toString().equals("acceptance")) return;
      if (!active.compareAndSet(false, true)) return;
      new Observer(project, output.toRealPath(), runId, suite, suppliedFixture).start();
    } catch (Exception error) {
      // Only an explicitly correlated managed run gets a diagnostic. No arbitrary/daily output.
      String runId=System.getProperty("anotherfsm.acceptance.runId", "");
      if(!runId.isEmpty()&&runId.equals(System.getenv("ANOTHERFSM_ACCEPTANCE_RUN_ID")))try {
        Path plugins=Path.of(System.getProperty("idea.plugins.path")).toRealPath();
        Path repository=plugins.getParent().getParent().getParent();
        Path output=Path.of(System.getProperty("anotherfsm.acceptance.dir")).toRealPath();
        if(plugins.getParent().getFileName().toString().equals(".sandbox")&&output.startsWith(repository.resolve(".scratch")))
          Files.writeString(output.resolve("lifecycle-startup-error.json"),new Gson().toJson(Map.of("runId",runId,"error",error.toString())));
      }catch(Exception ignored) { /* lack of owned diagnostic path is not permission to write elsewhere */ }
    }
  }

  private static final class Observer {
    final Project project;
    final Path output;
    final String runId, suite, fixture;
    final String caseId = System.getProperty("anotherfsm.acceptance.case", "all");
    final long pid = ProcessHandle.current().pid();
    final long startedEpochMs = ProcessHandle.current().info().startInstant().orElseThrow().toEpochMilli();
    final AtomicBoolean exitQueued = new AtomicBoolean();
    final AtomicBoolean dialogQueued = new AtomicBoolean();
    final AtomicBoolean cancellationQueued = new AtomicBoolean();
    long seq;
    boolean exitRequested;
    Observer(Project project, Path output, String runId, String suite, String fixture) {
      this.project = project; this.output = output; this.runId = runId; this.suite = suite; this.fixture = fixture;
    }
    void start() throws Exception {
      heartbeat(); // Immediate independent startup evidence, before any scheduled test activity.
      AppExecutorUtil.getAppScheduledExecutorService().scheduleWithFixedDelay(() -> {
        if (ApplicationManager.getApplication().isDisposed()) return;
        try { heartbeat(); } catch (Exception ignored) { /* stale heartbeat is observable by supervisor */ }
        try { requestExit(); } catch (Exception ignored) { /* replacement/read sharing race: retry */ }
        if (exitRequested && dialogQueued.compareAndSet(false, true)) {
          ApplicationManager.getApplication().invokeLater(() -> {
            try { confirmOwnExitOnly(); } finally { dialogQueued.set(false); }
          }, ModalityState.any());
        }
      }, 250, 250, TimeUnit.MILLISECONDS);
    }
    void heartbeat() throws Exception {
      write("lifecycle.json", Map.of("runId", runId, "suite", suite, "case", caseId, "fixture", fixture,
          "frontendPid", pid, "frontendStartedEpochMs", startedEpochMs, "seq", ++seq,
          "projectDisposed", project.isDisposed(), "heartbeatEpochMs", System.currentTimeMillis()));
    }
    void write(String name, Object value) throws Exception {
      Path temporary = output.resolve(name + "." + UUID.randomUUID() + ".tmp");
      try {
        Files.writeString(temporary, new Gson().toJson(value));
        Files.move(temporary, output.resolve(name), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
      } finally { Files.deleteIfExists(temporary); }
    }
    void requestExit() throws Exception {
      var request = JsonParser.parseString(Files.readString(output.resolve("exit-request.json"))).getAsJsonObject();
      if (!request.get("runId").getAsString().equals(runId) || !request.get("action").getAsString().equals("exit")
          || request.get("frontendPid").getAsLong() != pid) return;
      // Windows FILETIME has 100ns precision; ProcessHandle exposes startInstant in milliseconds.
      // The supervisor checks the full FILETIME before writing; this JVM checks its own start time too.
      long creation = request.get("creationFileTime").getAsLong();
      if (creation <= 0 || creation / 10_000L - 11_644_473_600_000L != startedEpochMs) return;
      exitRequested = true;
      if (cancellationQueued.compareAndSet(false, true)) {
        ApplicationManager.getApplication().invokeLater(() -> {
          for (var participant : cancellations) if (participant.project() == project && !participant.clean().getAsBoolean())
            participant.cancel().run();
        }, ModalityState.any());
      }
      // Boundary project restoration can be asynchronous. Never close/save the IDE over
      // test-owned dirty buffers before their normal guarded cleanup has completed.
      ApplicationManager.getApplication().invokeLater(() -> {
        if (cancellations.stream().anyMatch(participant -> participant.project() == project && !participant.clean().getAsBoolean())) return;
        if (exitQueued.compareAndSet(false, true)) {
          try { write("exit-observation.json", Map.of("runId", runId, "frontendPid", pid, "event", "participants cleaned; public Application.exit requested")); }
          catch (Exception ignored) { exitQueued.set(false); return; }
          ApplicationManager.getApplication().exit();
        }
      }, ModalityState.any());
    }
    List<Component> tree(Component component) {
      var result = new ArrayList<Component>(); result.add(component);
      if (component instanceof Container container) for (var child : container.getComponents()) result.addAll(tree(child));
      return result;
    }
    String label(String text) { return text == null ? "" : text.replace("&", "").replace("_", "").trim(); }
    void confirmOwnExitOnly() {
      // Window.getWindows contains only windows actually seen in THIS JVM. No OS automation.
      for (Window window : Window.getWindows()) {
        if (!(window instanceof JDialog dialog) || !dialog.isShowing() || !dialog.isModal()
            || !List.of("Confirm Exit", "Exit").contains(dialog.getTitle())) continue;
        var components = tree(dialog);
        String text = components.stream().map(c -> c instanceof JLabel label ? label.getText()
            : c instanceof JTextComponent content ? content.getText() : null)
            .filter(s -> s != null).reduce("", (a, b) -> a + " " + b);
        if (!text.matches("(?is).*(are you sure you want to exit|do you want to exit|exit rider).*")) continue;
        if (text.matches("(?is).*(save|discard|trust|decompil|legal|unsaved).*")) continue;
        var buttons = components.stream().filter(c -> c instanceof javax.swing.JButton)
            .map(c -> (AbstractButton)c).toList();
        if (buttons.stream().anyMatch(b -> !List.of("Exit", "Cancel").contains(label(b.getText())))) continue;
        var exits = buttons.stream().filter(b -> label(b.getText()).equals("Exit") && b.isEnabled() && b.isShowing()).toList();
        if (exits.size() == 1) {
          try { write("exit-dialog-observation.json", Map.of("runId", runId, "frontendPid", pid, "title", dialog.getTitle(), "text", text)); }
          catch (Exception ignored) { return; }
          exits.get(0).doClick();
        }
      }
    }
  }
}
