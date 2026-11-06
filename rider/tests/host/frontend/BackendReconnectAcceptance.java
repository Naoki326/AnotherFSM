package anotherfsm.acceptance;

import anotherfsm.BackendConnection;
import com.google.gson.*;
import com.intellij.ide.DataManager;
import com.intellij.notification.Notification;
import com.intellij.notification.NotificationAction;
import com.intellij.notification.NotificationsManager;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.actionSystem.*;
import com.intellij.openapi.application.ApplicationInfo;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.application.ModalityState;
import com.intellij.openapi.editor.Document;
import com.intellij.openapi.editor.Editor;
import com.intellij.openapi.fileEditor.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.util.Disposer;
import com.intellij.openapi.vfs.*;
import com.intellij.util.concurrency.AppExecutorUtil;
import com.jetbrains.rd.util.lifetime.Lifetime;
import com.jetbrains.rd.framework.SocketWire;
import com.jetbrains.rider.protocol.DelayedServerWire;
import java.net.Socket;
import com.jetbrains.rd.util.lifetime.LifetimeDefinition;
import com.jetbrains.rd.util.lifetime.LifetimeStatus;
import com.jetbrains.rider.projectView.SolutionHostExtensionsKt;
import com.jetbrains.rider.protocol.ApplicationProtocolKt;
import com.jetbrains.rider.protocol.IProtocolHost;
import com.jetbrains.rider.protocol.IProtocolHostWithBackend;
import java.lang.ref.WeakReference;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import kotlin.Unit;

/**
 * Separate test-observer plugin only; HostAcceptance calls start after its full journey passes.
 * No host/service construction, guessed restart action, protocol mutation or business execution.
 * Default fault closes the current loopback socket; opt-in crash mode targets this JVM's own backend only.
 * No buffer edits/saves; no claim of packet reordering or a deterministically outstanding old RPC.
 */
public final class BackendReconnectAcceptance {
  private BackendReconnectAcceptance() {}

  public static void start(Project project, Path output, Path fixtureRoot) {
    Objects.requireNonNull(project);
    Objects.requireNonNull(output);
    Objects.requireNonNull(fixtureRoot);
    new Journey(project, output, fixtureRoot).start();
  }

  private enum Stage { BASELINE, INJECT, DISCONNECTED, RECOVER, RESOLVE, QUIET, NAVIGATE, VERIFY }

  private static final class Journey {
    // Test expectation only: no implementation of product key/type mappings.
    static final String EXPECTED_TYPE = "Orders.Container+LookupWorker";
    static final long LIMIT_MS = 180_000;
    final Project project;
    final boolean transportOnly=Boolean.getBoolean("anotherfsm.transportReconnectAcceptance")&&!Boolean.getBoolean("anotherfsm.backendCrashAcceptance");
    Socket oldSocket;
    final AtomicBoolean wireRecovered=new AtomicBoolean();
    volatile Throwable transportFailure;
    final Path output, suppliedRoot;
    final long jvmPid = ProcessHandle.current().pid();
    final long begun = System.nanoTime();
    final AtomicBoolean tickQueued = new AtomicBoolean();
    final AtomicBoolean oldWireFalse = new AtomicBoolean();
    final ConcurrentLinkedQueue<JsonObject> rdEvents = new ConcurrentLinkedQueue<>();
    final LifetimeDefinition observation = new LifetimeDefinition();
    final List<JsonObject> events = new ArrayList<>();
    final Set<Notification> preexistingNotifications = Collections.newSetFromMap(new IdentityHashMap<>());
    final Set<Notification> recordedNotifications = Collections.newSetFromMap(new IdentityHashMap<>());
    final Gson gson = new GsonBuilder().setPrettyPrinting().create();
    ScheduledFuture<?> timer;
    volatile boolean done;
    volatile Stage stage = Stage.BASELINE;
    long stageStart = begun, injectedAt, injectedWall, nextProbe, queryStarted, queryGeneration, navigationSeen;
    long oldPid = -1, newPid = -1;
    IProtocolHost oldHost, newHost;
    Lifetime oldBackendLifetime;
    ProcessHandle oldProcess;
    Path root;
    VirtualFile script, nodes;
    Document document;
    Editor editor;
    String originalText, baselineSession, newSession, lastSnapshot;
    long originalVersion;
    int offset;
    JsonObject baselineTarget;
    Disposable query, oldRequest;
    volatile boolean injected;
    boolean initialized, inFlight, oldRequestCancelled, transportInvalidated;
    boolean recoveryInvoked, deathRecorded, disconnectRecorded;

    Journey(Project project, Path output, Path fixtureRoot) {
      this.project = project; this.output = output; suppliedRoot = fixtureRoot;
    }

    void start() {
      timer = AppExecutorUtil.getAppScheduledExecutorService().scheduleWithFixedDelay(() -> {
        if (done || !tickQueued.compareAndSet(false, true)) return;
        ApplicationManager.getApplication().invokeLater(() -> {
          try { tick(); }
          catch (Throwable failure) { finish(false, failure.toString()); }
          finally { tickQueued.set(false); }
        }, ModalityState.any());
      }, 0, 250, TimeUnit.MILLISECONDS);
      if (done) timer.cancel(false);
    }

    long since(long time) { return (System.nanoTime() - time) / 1_000_000; }
    void require(boolean value, String reason) {
      if (!value) throw new IllegalStateException(reason);
    }
    JsonObject event(String name, Object value) {
      JsonObject item = new JsonObject();
      item.addProperty("elapsedMs", since(begun)); item.addProperty("stage", stage.name());
      item.addProperty("name", name); item.add("value", gson.toJsonTree(value));
      return item;
    }
    void record(String name, Object value) { events.add(event(name, value)); }
    void next(Stage value) { stage = value; stageStart = System.nanoTime(); report("running", null); }

    void validateFixture() throws Exception {
      require(Boolean.getBoolean("anotherfsm.acceptance"), "Fault injection requires anotherfsm.acceptance=true");
      require(!project.isDisposed() && !project.isDefault(), "Only a live non-default fixture project is permitted");
      String configured = System.getProperty("anotherfsm.fixture.root");
      require(configured != null && project.getBasePath() != null, "Missing configured fixture/project root");
      Path actualRoot = suppliedRoot.toRealPath();
      require(actualRoot.equals(Path.of(configured).toRealPath()), "fixtureRoot differs from acceptance configuration");
      require(actualRoot.equals(Path.of(project.getBasePath()).toRealPath()), "Project base path is not fixtureRoot");
      for (String name : List.of("Orders/orders.fsm", "Orders/Nodes.cs"))
        require(actualRoot.resolve(name).toRealPath().startsWith(actualRoot), "Fixture file escapes root: " + name);
      root = actualRoot;
    }

    void initialize() throws Exception {
      validateFixture();
      String build = ApplicationInfo.getInstance().getBuild().asStringWithoutProductCode();
      require("262.10968.170".equals(build), "This acceptance targets Rider 262.10968.170, got " + build);
      script = LocalFileSystem.getInstance().refreshAndFindFileByNioFile(root.resolve("Orders/orders.fsm"));
      nodes = LocalFileSystem.getInstance().refreshAndFindFileByNioFile(root.resolve("Orders/Nodes.cs"));
      require(script != null && nodes != null, "Fixture files unavailable in VFS");
      document = FileDocumentManager.getInstance().getDocument(script);
      require(document != null, "No actual fixture document");
      originalText = document.getText(); originalVersion = document.getModificationStamp();
      offset = originalText.indexOf("BusinessQuery)", originalText.indexOf("def Query("));
      require(originalText.contains("def Query(BusinessQuery)") && offset >= 0, "Missing fixture definition marker");
      editor = FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project, script, offset), true);
      require(editor != null, "Cannot open actual fixture editor");
      editor.getCaretModel().moveToOffset(offset); editor.getSelectionModel().removeSelection();
      preexistingNotifications.addAll(Arrays.asList(notifications()));
      // Public API has no unsubscription handle; do not retain the completed journey in the service.
      WeakReference<Journey> weak = new WeakReference<>(this);
      BackendConnection.get(project).onInvalidated(raw -> {
        Journey journey = weak.get();
        if (journey == null || journey.done) return;
        try {
          journey.record("invalidated-raw", raw);
          if (journey.injected) {
            JsonObject value = JsonParser.parseString(raw).getAsJsonObject();
            if (value.has("transportInvalidated") && value.get("transportInvalidated").getAsBoolean())
              journey.transportInvalidated = true;
            journey.cancelOldRequest("actual invalidation");
          }
          journey.report("running", null);
        } catch (Throwable failure) { journey.finish(false, failure.toString()); }
      });
      record("environment", Map.of("riderBuild", build, "jvmPid", jvmPid, "fixtureRoot", root.toString(),
          "bufferModifiedByTest", false, "networkReorderingInjected", false));
      initialized = true; report("running", null);
    }

    IProtocolHost host() { return ApplicationProtocolKt.getProtocolHostIfExists(project); }
    long pid(IProtocolHost host) {
      try { return host instanceof IProtocolHostWithBackend backend ? backend.getResharperProcess().getBackendPid() : -1; }
      catch (RuntimeException unavailable) { return -1; }
    }
    boolean wired(IProtocolHost host) {
      return host != null && Boolean.TRUE.equals(host.getProtocol().getWire().getConnected().getValue());
    }
    JsonObject snapshot(IProtocolHost host) {
      JsonObject value = new JsonObject();
      value.addProperty("jvmPid", ProcessHandle.current().pid());
      value.addProperty("hasSolution", !project.isDisposed() && SolutionHostExtensionsKt.getHasSolution(project));
      if (host == null) { value.addProperty("host", (String)null); return value; }
      value.addProperty("host", host.getId());
      value.addProperty("hostIdentity", Integer.toHexString(System.identityHashCode(host)));
      value.addProperty("hostProjectMatches", host.getProject().getValue() == project);
      value.addProperty("hostLifetime", host.getLifetime().getStatus().name());
      value.addProperty("wireConnected", wired(host)); value.addProperty("backendPid", pid(host));
      if (host instanceof IProtocolHostWithBackend backend)
        value.addProperty("backendProcessLifetime", backend.getResharperProcess().getBackendProcessLifetime().getStatus().name());
      return value;
    }
    boolean readyCandidate(IProtocolHost host) {
      return host instanceof IProtocolHostWithBackend && host.getProject().getValue() == project
          && host.getLifetime().getStatus() == LifetimeStatus.Alive && wired(host) && pid(host) > 0
          && SolutionHostExtensionsKt.getHasSolution(project);
    }

    JsonObject request() {
      JsonObject value = new JsonObject();
      value.addProperty("action", "resolve"); value.addProperty("path", script.getPath());
      value.addProperty("text", document.getText()); value.addProperty("documentVersion", document.getModificationStamp());
      value.addProperty("offset", offset); value.addProperty("requestId", UUID.randomUUID().toString());
      value.addProperty("contextVersion", -1);
      return value;
    }

    void probe(boolean baseline) {
      if (inFlight) {
        if (since(queryStarted) > 6_000) {
          queryGeneration++; inFlight = false;
          if (query != null) Disposer.dispose(query);
          query = null; record("query-timeout-handle-disposed", baseline ? "baseline" : "reconnected");
        }
        return;
      }
      if (System.nanoTime() < nextProbe) return;
      IProtocolHost candidate = host();
      if (!readyCandidate(candidate) || (!baseline && !transportOnly && pid(candidate) == oldPid)) return;
      long candidatePid = pid(candidate), generation = ++queryGeneration;
      JsonObject envelope = request();
      inFlight = true; queryStarted = System.nanoTime(); nextProbe = queryStarted + TimeUnit.MILLISECONDS.toNanos(750);
      record("query-submitted", envelope);
      query = BackendConnection.get(project).query(envelope.toString(), raw -> {
        if (done) return;
        try {
          record("query-result-raw", raw);
          if (generation != queryGeneration) { record("ignored-old-query-result", generation); return; }
          inFlight = false;
          JsonObject reply = JsonParser.parseString(raw).getAsJsonObject();
          require(envelope.get("requestId").equals(reply.get("requestId")), "RD reply requestId mismatch");
          require(envelope.get("documentVersion").equals(reply.get("documentVersion")), "RD reply documentVersion mismatch");
          if (host() != candidate || pid(candidate) != candidatePid || !readyCandidate(candidate)) {
            record("ignored-query-from-changed-host", snapshot(candidate)); return;
          }
          if (!reply.has("status") || !"type".equals(reply.get("status").getAsString())) {
            record("semantic-query-not-ready", reply); return;
          }
          JsonObject target = reply.getAsJsonObject("target");
          require(target != null && EXPECTED_TYPE.equals(target.get("clrName").getAsString()), "Wrong actual RD type target: " + reply);
          require(target.has("module") && !target.get("module").isJsonNull()
              && target.has("context") && !target.get("context").isJsonNull(), "RD target lacks module/context identity");
          String session = reply.get("session").getAsString();
          require(!session.isBlank() && reply.get("contextVersion").getAsLong() >= 0, "Missing semantic session/contextVersion");
          if (baseline) {
            baselineSession = session; baselineTarget = target.deepCopy(); oldHost = candidate; oldPid = candidatePid;
            oldBackendLifetime = ((IProtocolHostWithBackend)candidate).getResharperProcess().getBackendProcessLifetime();
            record("baseline-host", snapshot(oldHost)); record("baseline-session", session); record("baseline-target", target);
            observeOldWire(); next(Stage.INJECT);
          } else {
            require(transportOnly || !session.equals(baselineSession), "Replacement backend reused old semantic session");
            for (String key : List.of("module", "context", "clrName"))
              require(baselineTarget.get(key).equals(target.get(key)), "Recovered target identity changed: " + reply);
            newHost = candidate; newPid = candidatePid; newSession = session;
            record("reconnected-host", snapshot(candidate)); record("reconnected-session", session);
            record("fresh-actual-rd-result", reply); next(Stage.QUIET);
          }
        } catch (Throwable failure) { finish(false, failure.toString()); }
      }, failure -> {
        if (done) return;
        record("query-failure", failure.toString());
        if (generation == queryGeneration) inFlight = false;
      });
    }

    void observeOldWire() {
      WeakReference<Journey> weak = new WeakReference<>(this);
      oldHost.getLifetime().onTerminationIfAlive(() -> {
        Journey journey = weak.get();
        if (journey != null && !journey.done)
          journey.rdEvents.add(journey.event("old-host-lifetime-terminated", true));
        return Unit.INSTANCE;
      });
      oldHost.getProtocol().getScheduler().queue(() -> {
        if (!done && observation.getStatus() == LifetimeStatus.Alive)
          oldHost.getProtocol().getWire().getConnected().advise(observation, connected -> {
            if (injected && !connected) oldWireFalse.set(true);
            if (injected && connected && oldWireFalse.get()) wireRecovered.set(true);
            rdEvents.add(event("old-wire-connected-raw", connected)); return Unit.INSTANCE;
          });
        return Unit.INSTANCE;
      });
    }

    void action() {
      AnAction action = ActionManager.getInstance().getAction("AnotherFSM.GoToDeclaration");
      require(action != null, "Missing registered AnotherFSM.GoToDeclaration action");
      DataContext context = DataManager.getInstance().getDataContext(editor.getContentComponent());
      require(CommonDataKeys.EDITOR.getData(context) == editor && CommonDataKeys.PROJECT.getData(context) == project,
          "Registered navigation action lacks actual editor/project context");
      record("registered-navigation-action", "AnotherFSM.GoToDeclaration");
      action.actionPerformed(AnActionEvent.createFromAnAction(action, null, ActionPlaces.EDITOR_POPUP, context));
    }

    void inject() throws Exception {
      validateFixture();
      require(host() == oldHost && readyCandidate(oldHost) && pid(oldHost) == oldPid,
          "Baseline project host/PID/wire changed before fault injection");
      require(oldPid != jvmPid && ProcessHandle.current().pid() == jvmPid, "Cannot target the test JVM");
      oldProcess = ProcessHandle.of(oldPid).orElseThrow(() -> new IllegalStateException("Backend PID no longer exists"));
      require(oldProcess.isAlive() && oldProcess.parent().map(parent -> parent.pid() == jvmPid).orElse(false),
          "Refusing fault injection: backend is not this test JVM's direct child");
      require(oldBackendLifetime.getStatus() == LifetimeStatus.Alive,
          "Old backend process lifetime is already terminated");
      record("pre-injection-host", snapshot(oldHost));
      record("pre-injection-process", Map.of("pid", oldPid, "parentPid", jvmPid,
          "startInstant", oldProcess.info().startInstant().map(Object::toString).orElse("unavailable"), "session", baselineSession));
      // Queue real old-epoch work. It may finish, fail, or be cancelled; do not invent an in-flight RPC.
      oldRequest = BackendConnection.get(project).query(request().toString(), raw -> {
        if (done) return;
        record("old-request-result-not-used-for-recovery", raw);
        if (oldRequestCancelled) finish(false, "Old request delivered success after its handle was cancelled");
      }, failure -> { if (!done) record("old-request-failure", failure.toString()); });
      if(!transportOnly) action();
      requireScriptUnchangedAndSelected();
      // Repeat every destructive gate immediately before the one and only kill. Never traverse children.
      validateFixture();
      require(!done && since(begun) < LIMIT_MS, "Acceptance ended before fault injection");
      require(host() == oldHost && readyCandidate(oldHost) && pid(oldHost) == oldPid
          && oldBackendLifetime.getStatus() == LifetimeStatus.Alive,
          "Project host/PID/wire changed while queuing old work");
      require(oldPid != ProcessHandle.current().pid() && oldProcess.isAlive()
          && oldProcess.parent().map(parent -> parent.pid() == ProcessHandle.current().pid()).orElse(false),
          "Backend parent/PID safety gate changed");
      injected = true; injectedAt = System.nanoTime(); injectedWall = System.currentTimeMillis();
      record("fault-injection-requested", transportOnly?"Socket.close on the existing project transport only":"ProcessHandle.destroyForcibly on validated direct child only");
      report("running", null);
      if(transportOnly) {
        oldSocket=socket(oldHost);
        require(oldSocket!=null&&!oldSocket.isClosed()&&oldSocket.getInetAddress().isLoopbackAddress()
            &&oldSocket.getLocalAddress().isLoopbackAddress(),"Not the expected live loopback project socket");
        record("old-socket",Map.of("identity",System.identityHashCode(oldSocket),"local",oldSocket.getLocalSocketAddress().toString(),"remote",oldSocket.getRemoteSocketAddress().toString()));
        CompletableFuture.runAsync(() -> {try {oldSocket.close();} catch(Throwable failure){transportFailure=failure;}});
      } else {
        boolean accepted = oldProcess.destroyForcibly();
        record("destroyForcibly-returned", accepted);
        require(accepted, "OS refused controlled backend fault injection");
      }
      next(Stage.DISCONNECTED);
    }

    Socket socket(IProtocolHost host) {
      var wire=host.getProtocol().getWire();
      if(wire instanceof DelayedServerWire delayed)return delayed.getRealWire().getSocketProvider().getValueOrNull();
      if(wire instanceof SocketWire.Base base)return base.getSocketProvider().getValueOrNull();
      throw new IllegalStateException("Unsupported actual host wire: "+wire.getClass());
    }

    void cancelOldRequest(String reason) {
      if (oldRequest == null || oldRequestCancelled) return;
      oldRequestCancelled = true; Disposer.dispose(oldRequest);
      record("old-request-handle-disposed", reason + "; cancellation requested, not proof of an outstanding RPC");
    }

    Notification[] notifications() {
      return NotificationsManager.getNotificationsManager().getNotificationsOfType(Notification.class, project);
    }

    void recoverFromActualNotification() {
      if (recoveryInvoked || since(injectedAt) < 15_000) return;
      for (Notification notification : notifications()) {
        if (notification.isExpired() || preexistingNotifications.contains(notification) || notification.getTimestamp() < injectedWall) continue;
        String content = notification.getTitle() + " " + notification.getContent();
        if (!content.toLowerCase(Locale.ROOT).matches("(?s).*(backend|resharper).*")) continue;
        List<String> labels = new ArrayList<>();
        for (AnAction candidate : notification.getActions()) labels.add(candidate.getTemplatePresentation().getText());
        if (recordedNotifications.add(notification))
          record("actual-backend-notification", Map.of("title", notification.getTitle(), "content", notification.getContent(), "actions", labels));
        for (AnAction candidate : notification.getActions()) {
          String label = candidate.getTemplatePresentation().getText();
          if (!(candidate instanceof NotificationAction restart) || label == null) continue;
          String normalized = label.replace("&", "").replace("_", "").trim();
          if (!normalized.matches("(?i)^(restart|relaunch)(?:\\s+(?:the\\s+)?(?:backend|resharper(?:\\s+backend)?))?[.!…]*$")) continue;
          if (!candidate.getTemplatePresentation().isEnabledAndVisible()) continue;
          DataContext editorContext = DataManager.getInstance().getDataContext(editor.getContentComponent());
          DataContext context = key -> Notification.KEY.is(key) ? notification
              : CommonDataKeys.PROJECT.is(key) ? project : editorContext.getData(key);
          AnActionEvent event = AnActionEvent.createFromAnAction(candidate, null, ActionPlaces.UNKNOWN, context);
          require(event.getProject() == project && Notification.get(event) == notification, "Recovery action lost notification/project context");
          recoveryInvoked = true;
          record("actual-notification-recovery-action", Map.of("text", label, "class", candidate.getClass().getName()));
          report("running", null);
          restart.actionPerformed(event, notification);
          return;
        }
      }
    }

    void requireScriptUnchangedAndSelected() {
      require(!editor.isDisposed() && document.getText().equals(originalText)
          && document.getModificationStamp() == originalVersion, "Fixture buffer changed during reconnect acceptance");
      Editor selected = FileEditorManager.getInstance(project).getSelectedTextEditor();
      require(selected == editor && editor.getCaretModel().getOffset() == offset && !editor.getSelectionModel().hasSelection(),
          "Unexpected navigation/caret change before fresh reconnected action (possible old-epoch jump)");
    }

    boolean navigatedToExpectedDeclaration() {
      Editor selected = FileEditorManager.getInstance(project).getSelectedTextEditor();
      if (selected == null || !nodes.equals(FileDocumentManager.getInstance().getFile(selected.getDocument()))) return false;
      Document target = selected.getDocument(); int position = selected.getCaretModel().getOffset();
      int line = target.getLineNumber(position);
      String text = target.getText().substring(target.getLineStartOffset(line), target.getLineEndOffset(line));
      return text.contains("class LookupWorker");
    }

    void tick() throws Exception {
      if (done) return;
      if (since(begun) >= LIMIT_MS) {
        finish(false, "180s deadline at " + stage + (transportOnly
            ? "; require real wire false/true, different socket, live original backend, real RD and registered navigation"
            : "; require dead old PID, real disconnect, replacement PID/session, real RD and registered navigation")); return;
      }
      if(transportFailure!=null)throw new IllegalStateException("Transport close failed",transportFailure);
      if (!initialized) initialize();
      require(!project.isDisposed() && ProcessHandle.current().pid() == jvmPid, "Project disposed or test JVM changed");
      JsonObject pending;
      while ((pending = rdEvents.poll()) != null) events.add(pending);
      JsonObject current = snapshot(host());
      if (oldHost != null) current.add("oldHost", snapshot(oldHost));
      if (oldBackendLifetime != null) current.addProperty("originalBackendProcessLifetime", oldBackendLifetime.getStatus().name());
      String serialized = current.toString();
      if (!serialized.equals(lastSnapshot)) { record("host-wire-snapshot", current); lastSnapshot = serialized; report("running", null); }
      if (injected && stage != Stage.NAVIGATE && stage != Stage.VERIFY) requireScriptUnchangedAndSelected();
      switch (stage) {
        case BASELINE -> probe(true);
        case INJECT -> inject();
        case DISCONNECTED -> {
          if (!oldProcess.isAlive() && !deathRecorded) { deathRecorded = true; record("old-process-observed-dead", oldPid); }
          boolean disconnected = oldWireFalse.get() || oldHost.getLifetime().getStatus() == LifetimeStatus.Terminated;
          if (disconnected && !disconnectRecorded) { disconnectRecorded = true; record("actual-disconnect", snapshot(oldHost)); cancelOldRequest("wire false / old host terminated"); }
          if (disconnectRecorded && (transportOnly?oldSocket.isClosed():deathRecorded)) next(Stage.RECOVER);
        }
        case RECOVER -> {
          IProtocolHost candidate = host();
          if (readyCandidate(candidate) && (transportOnly
              ?pid(candidate)==oldPid&&oldProcess.isAlive()&&wireRecovered.get()&&socket(candidate)!=oldSocket
              :pid(candidate)!=oldPid)) {
            record("replacement-candidate-not-semantic-readiness", snapshot(candidate)); next(Stage.RESOLVE);
          } else if (!transportOnly&&(candidate == null || pid(candidate) == oldPid || pid(candidate) <= 0
              || ProcessHandle.of(pid(candidate)).map(process -> !process.isAlive()).orElse(true))) {
            // A live replacement process is already an automatic recovery attempt; never restart it again.
            recoverFromActualNotification();
          }
        }
        case RESOLVE -> probe(false);
        case QUIET -> {
          require(host() == newHost && pid(newHost) == newPid && readyCandidate(newHost), "Recovered host changed before navigation");
          if (since(stageStart) >= 1_000) {
            require(transportInvalidated, "No actual post-fault transportInvalidated notification");
            require(oldRequestCancelled, "Old request handle was not disposed");
            record("no-old-epoch-navigation-observed", true); next(Stage.NAVIGATE);
          }
        }
        case NAVIGATE -> {
          requireScriptUnchangedAndSelected();
          next(Stage.VERIFY); // Before action: nested EDT callbacks cannot re-run this stage.
          action();
        }
        case VERIFY -> {
          require(host() == newHost && pid(newHost) == newPid && readyCandidate(newHost), "Recovered backend lost during navigation");
          if (navigatedToExpectedDeclaration()) {
            require(document.getText().equals(originalText) && document.getModificationStamp() == originalVersion, "Fixture buffer unexpectedly modified");
            if (navigationSeen == 0) {
              navigationSeen = System.nanoTime();
              record("actual-csharp-declaration-navigation", Map.of("path", nodes.getPath(), "session", newSession, "backendPid", newPid));
            } else if (since(navigationSeen) >= 1_000) {
              record("post-navigation-stability-ms", since(navigationSeen)); finish(true, null);
            }
          } else if (navigationSeen != 0) finish(false, "Navigation moved away from the confirmed C# declaration after recovery");
          else if (since(stageStart) > 15_000) finish(false, "Registered reconnected GoToDeclaration did not navigate to the actual LookupWorker declaration");
        }
      }
    }

    void report(String status, String error) {
      try {
        Files.createDirectories(output);
        JsonObject value = new JsonObject();
        value.addProperty("mode",transportOnly?"existing-socket-reconnect":"backend-process-crash");
        value.addProperty("wireRecoveredObserved",wireRecovered.get());
        value.addProperty("status", status); value.addProperty("stage", stage.name()); value.addProperty("elapsedMs", since(begun));
        value.addProperty("error", error); value.addProperty("jvmPid", jvmPid);
        value.addProperty("oldBackendPid", oldPid); value.addProperty("newBackendPid", newPid);
        value.addProperty("baselineSession", baselineSession); value.addProperty("reconnectedSession", newSession);
        value.addProperty("oldProcessDead", deathRecorded); value.addProperty("realDisconnectObserved", disconnectRecorded);
        value.addProperty("oldWireFalseObserved", oldWireFalse.get()); value.addProperty("transportInvalidatedObserved", transportInvalidated);
        value.addProperty("oldRequestHandleDisposed", oldRequestCancelled); value.addProperty("notificationRecoveryInvoked", recoveryInvoked);
        value.add("observations", gson.toJsonTree(events));
        Files.writeString(output.resolve("host-reconnect.json"), gson.toJson(value));
      } catch (Exception failure) { failure.printStackTrace(); }
    }

    void finish(boolean passed, String error) {
      if (done) return;
      done = true;
      if (timer != null) timer.cancel(false);
      queryGeneration++;
      try {
        if (query != null) Disposer.dispose(query);
        cancelOldRequest("test cleanup");
        observation.terminate(true);
      } catch (Throwable failure) { passed = false; error = String.valueOf(error) + "; cleanup: " + failure; }
      JsonObject pending;
      while ((pending = rdEvents.poll()) != null) events.add(pending);
      report(passed ? "passed" : "failed", error);
    }
  }
}
