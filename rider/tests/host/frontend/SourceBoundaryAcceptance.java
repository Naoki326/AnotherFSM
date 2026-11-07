package anotherfsm.acceptance;

import anotherfsm.BackendConnection;
import com.google.gson.*;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.util.Disposer;
import com.intellij.ide.DataManager;
import com.intellij.notification.*;
import com.intellij.openapi.actionSystem.*;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.editor.Editor;
import com.intellij.openapi.fileEditor.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.LocalFileSystem;
import com.intellij.util.concurrency.AppExecutorUtil;
import java.awt.*;
import java.net.*;
import java.nio.file.*;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.*;
import java.util.List;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.*;
import javax.imageio.ImageIO;

/** Explicit source-only journey, not a StartupActivity. Main calls after verified sandbox startup,
 * sequentially, never alongside ComplexBoundaryAcceptance. No build/restore/IDE launch, private
 * fields, direct Navigate/provider calls or synthetic approvals. Only the separate test setup
 * receives fixed, nonce-correlated commands. Main supplies a real legal-dialog click witness and
 * restores exact original sandbox DotSettings AFTER shutting down. Callback is completion without
 * assertion failure, NOT full AC pass; pending cases and post-shutdown restore remain in the report.
 */
public final class SourceBoundaryAcceptance {
  private SourceBoundaryAcceptance() {}
  public static void start(Project project,Path output,Path root,Runnable onSuccess) {
    ApplicationManager.getApplication().invokeLater(() -> new Journey(project,output,root,onSuccess).start());
  }
  private record Target(String fixture,String node,String key,String type) {}
  private static final List<Target> TARGETS=List.of(
      new Target("offline","OfflineEntry","SourceOfflineKey","OfflineWorker"),
      new Target("unauthorized","RestrictedEntry","SourceUnauthorizedKey","RestrictedWorker"),
      new Target("mismatch","MismatchEntry","SourceMismatchKey","MismatchWorker"),
      new Target("nosymbols","SymbolLessEntry","SourceNoSymbolsKey","SymbolLessWorker"),
      new Target("broken","BrokenEntry","SourceBrokenILKey","BrokenWorker"));
  private static final class Step {
    final String name;final Runnable enter,timeout;final BooleanSupplier verify;final long limit;
    String status="notRun",detail;long begun;
    Step(String name,Runnable enter,BooleanSupplier verify,long limit,Runnable timeout) {this.name=name;this.enter=enter;this.verify=verify;this.limit=limit;this.timeout=timeout;}
  }
  private static final class Journey {
    final Project project;final Path output,root,assets;final Runnable continuation;final String runId=UUID.randomUUID().toString();
    final Gson gson=new GsonBuilder().setPrettyPrinting().create();final List<Step> steps=new ArrayList<>();final List<Object> events=new ArrayList<>();
    final List<String> gaps=new ArrayList<>();final AtomicBoolean queued=new AtomicBoolean();final long started=System.nanoTime();
    final Set<Notification> seen=Collections.newSetFromMap(new IdentityHashMap<>());
    ScheduledFuture<?> timer;int index;boolean done,isolated,disabledReady,enabledReady,consentReady;
    JsonObject prepared,before,firstStable;String commandId,reason;long backendPid=-1,afterNotificationSequence,stableSeq;
    Future<JsonObject> serviceProbe;final Map<String,JsonObject> serviceEvidence=new HashMap<>();Editor origin;
    Disposable warmRequest;JsonObject warmReply;boolean warmSent;long warmSequence=-1;
    Journey(Project project,Path output,Path root,Runnable continuation) {
      this.project=project;this.output=output.toAbsolutePath().normalize();this.root=root.toAbsolutePath().normalize();this.assets=this.root.resolve("SourceCases");this.continuation=continuation;
      gaps.add("Exact DotSettings restoration is main's post-shutdown step, not a runtime/default-value restore. Read restoration receipt separately.");
      gaps.add("Controlled loopback refusal/HTTP401 plus a formal action is not whole-machine offline, real private-feed authorization, or proof the host attempted source fetching. Sources/PDB/Source Link providers are never selected by this journey.");
      specify();
    }
    void require(boolean value,String text) {if(!value)throw new IllegalStateException(text);}
    long ms(long begun) {return (System.nanoTime()-begun)/1_000_000;}
    String sha(byte[] bytes) {try{return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(bytes));}catch(Exception error){throw new IllegalStateException(error);}}
    void event(String kind,Object data) {events.add(Map.of("step",index,"elapsedMs",ms(started),"kind",kind,"value",data));}
    void pending(String detail) {var step=steps.get(index);step.status="pendingVerification";step.detail=detail;event("pending",detail);}
    void step(String name,Runnable enter,BooleanSupplier verify,long limit,Runnable timeout) {steps.add(new Step(name,enter,verify,limit,timeout));}
    void step(String name,Runnable enter,BooleanSupplier verify) {step(name,enter,verify,25_000,null);}
    void start() {
      AcceptanceLifecycle.onCancellation(project,() -> finish(false,"Supervisor requested cancellation"),() -> done);
      report("running",null);timer=AppExecutorUtil.getAppScheduledExecutorService().scheduleWithFixedDelay(() -> {
        if(done||!queued.compareAndSet(false,true))return;
        ApplicationManager.getApplication().invokeLater(() -> {try{tick();}catch(Throwable failure){finish(false,failure.toString());}finally{queued.set(false);}});
      },0,250,TimeUnit.MILLISECONDS);
    }
    void tick() {
      if(done)return;require(!project.isDisposed(),"Source boundary project disposed");sentinel();
      if(index==steps.size()){finish(true,null);return;}require(ms(started)<1_000_000,"Source boundary total deadline");
      var step=steps.get(index);
      if(step.status.equals("notRun")){step.status="running";step.begun=System.nanoTime();event("begin",step.name);step.enter.run();report("running",null);return;}
      if(step.verify.getAsBoolean()){if(step.status.equals("running"))step.status="passed";event(step.status,step.name);index++;report("running",null);}
      else if(ms(step.begun)>step.limit) {
        if(step.timeout!=null){pending("Real prerequisite/result not established before deadline; no inferred pass.");step.timeout.run();index++;report("running",null);}
        else throw new IllegalStateException("Deadline: "+step.name);
      }
    }
    JsonObject json(Path path) {try{return Files.isRegularFile(path)?JsonParser.parseString(Files.readString(path)).getAsJsonObject():null;}catch(java.io.IOException|JsonParseException retry){return null;}}
    void write(String filename,JsonObject value) {
      try {Files.createDirectories(output);Files.writeString(output.resolve(filename+".tmp"),gson.toJson(value));Files.move(output.resolve(filename+".tmp"),output.resolve(filename),StandardCopyOption.REPLACE_EXISTING);}
      catch(Exception error){throw new IllegalStateException(error);}
    }
    void sentinel() {
      String marker=System.getenv("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");require(marker!=null&&!marker.isBlank()&&Path.of(marker).isAbsolute(),"Configured absolute execution sentinel required");
      require(!Files.exists(Path.of(marker)),"Business execution marker exists; never deleted");
    }
    boolean same(Path left,Path right) {return left.toAbsolutePath().normalize().equals(right.toAbsolutePath().normalize());}
    boolean authorize() {
      if(isolated)return true;
      require(root.endsWith(Path.of("rider/tests/fixtures/boundary"))&&Files.isRegularFile(root.resolve("Boundary.sln")),"Owned Boundary.sln only");
      require(project.getBasePath()!=null&&same(Path.of(project.getBasePath()),root),"Wrong actual project");
      var rider=root.getParent().getParent().getParent();var box=rider.resolve(".sandbox");var properties=new JsonObject();
      var expected=new LinkedHashMap<String,Path>();for(var leaf:List.of("config","system","plugins"))expected.put("idea."+leaf+".path",box.resolve(leaf));
      expected.put("idea.log.path",rider.getParent().resolve(".scratch/issue-32/sandbox-ide"));
      for(var entry:expected.entrySet()) {
        String actual=System.getProperty(entry.getKey());require(actual!=null&&same(Path.of(actual),entry.getValue()),"Wrong sandbox property: "+entry.getKey());properties.addProperty(entry.getKey(),actual);
      }
      for(var key:List.of("idea.config.import.enabled","idea.settings.sync.enabled","idea.initially.ask.config")) {
        require("false".equals(System.getProperty(key)),"Import/sync not disabled");properties.addProperty(key,"false");
      }
      var launcher=json(box.resolve("launcher.json"));if(launcher==null||!launcher.has("windowsPid")||!launcher.has("creationFileTime")||launcher.get("windowsPid").getAsLong()!=ProcessHandle.current().pid())return false;
      require(same(Path.of(launcher.get("fixture").getAsString()),root.resolve("Boundary.sln"))&&same(Path.of(launcher.get("cwd").getAsString()),box),"Launcher actual fixture/cwd mismatch");
      boolean outputMatches=false;for(var value:launcher.getAsJsonArray("expandedArguments")) {
        String arg=value.getAsString();if(arg.startsWith("-Danotherfsm.acceptance.dir="))outputMatches=same(Path.of(arg.substring(arg.indexOf('=')+1)),output);
      }
      require(outputMatches&&Files.isRegularFile(box.resolve("prepared.json")),"Launcher output/preparation mismatch");
      var backup=json(box.resolve("source-boundary-settings-backup.json"));
      if(backup==null||!"active".equals(backup.get("state").getAsString())){pending("Main must back up exact sandbox DotSettings while closed before this launch. No setup command sent.");return false;}
      require(backup.get("confirmedClosed").getAsBoolean(),"Missing closed backup attestation");
      String backupId=backup.get("backupId").getAsString();UUID.fromString(backupId);
      try {
        String arg=launcher.getAsJsonArray("command").get(1).getAsString();require(arg.startsWith("@")&&sha(Files.readAllBytes(Path.of(arg.substring(1)))).equals(launcher.get("argfileSha256").getAsString()),"Actual argfile digest mismatch");
        require(sha(Files.readAllBytes(assets.resolve("artifacts/settings").resolve(backupId).resolve("GlobalSettingsStorage.bytes"))).equals(backup.get("sha256").getAsString()),"Backup bytes changed");
        prepared=json(assets.resolve("artifacts/prepared.json"));require(prepared!=null&&"prepared".equals(prepared.get("status").getAsString())&&prepared.get("sentinelAbsent").getAsBoolean(),"Prepare owned source assets separately first");
        for(var target:TARGETS)require(sha(Files.readAllBytes(assets.resolve("artifacts/dll/Issue34.Source."+target.fixture()+".dll"))).equals(prepared.getAsJsonObject("cases").getAsJsonObject(target.fixture()).get("dllSha256").getAsString()),"Prepared DLL changed: "+target.fixture());
        var proof=new JsonObject();proof.addProperty("runId",runId);proof.addProperty("root",root.toString());proof.addProperty("output",output.toString());proof.addProperty("frontendPid",ProcessHandle.current().pid());
        proof.addProperty("launcherSha256",sha(Files.readAllBytes(box.resolve("launcher.json"))));proof.addProperty("backupId",backupId);proof.add("properties",properties);
        write("source-policy-authorization.json",proof);event("verified-isolation-and-exact-backup",proof);isolated=true;return true;
      }catch(java.io.IOException error){throw new IllegalStateException(error);}
    }
    JsonObject sample() {
      var sample=json(output.resolve("source-policy-observation.json"));if(sample==null||!runId.equals(sample.get("runId").getAsString()))return null;
      if(backendPid<0)backendPid=sample.get("backendPid").getAsLong();require(backendPid==sample.get("backendPid").getAsLong(),"Backend process replaced");
      return sample.get("ready").getAsBoolean()&&sample.get("coherent").getAsBoolean()?sample:null;
    }
    JsonObject target(JsonObject sample,Target target) {
      if(sample!=null)for(var item:sample.getAsJsonArray("targets"))if(target.key().equals(item.getAsJsonObject().get("key").getAsString()))return item.getAsJsonObject();return null;
    }
    boolean determined(JsonObject sample,Target target) {var value=target(sample,target);return value!=null&&"Determined".equals(value.get("state").getAsString())&&value.get("compiled").getAsBoolean()&&!value.get("metadata").isJsonNull();}
    boolean applicable(JsonObject sample,Target target,String provider) {
      var value=target(sample,target);if(value!=null)for(var item:value.getAsJsonArray("providers")) {
        var entry=item.getAsJsonObject();if(provider.equals(entry.get("id").getAsString()))return entry.get("applicable").getAsBoolean();
      }return false;
    }
    boolean localOnly(JsonObject sample,boolean bodies) {
      if(sample==null)return false;var policy=sample.getAsJsonObject("policy");
      return policy.get("active").getAsBoolean()&&!policy.get("force").getAsBoolean()&&policy.get("effectiveBodies").getAsBoolean()==bodies
          &&!policy.get("download").getAsBoolean()&&!policy.get("useSymbols").getAsBoolean()&&!policy.get("sourceLink").getAsBoolean()
          &&TARGETS.stream().allMatch(target -> determined(sample,target)&&applicable(sample,target,"decompiler")==bodies);
    }
    void command(String operation) {
      require(isolated,"No setup before actual isolation");commandId=UUID.randomUUID().toString();var request=new JsonObject();request.addProperty("runId",runId);request.addProperty("commandId",commandId);
      request.addProperty("operation",operation);request.addProperty("createdUtc",Instant.now().toString());write("source-policy-command.json",request);event("explicit-test-setup-command",request);
    }
    JsonObject ack() {
      var ack=json(output.resolve("source-policy-ack.json"));if(ack==null||!runId.equals(ack.get("runId").getAsString())||!commandId.equals(ack.get("commandId").getAsString()))return null;
      require(ack.get("backendPid").getAsLong()==backendPid,"Wrong setup process");String status=ack.get("status").getAsString();require(!Set.of("failed","rejected").contains(status),"Setup rejected/failed: "+ack);return ack;
    }
    void policySetup(boolean bodies) {
      step("Explicit standalone setup: "+(bodies?"allow local decompiler":"disable decompiler")+", all three PDB/source switches false",() -> {
        firstStable=null;stableSeq=-1;command(bodies?"enable-decompile-local-only":"disable-decompile-and-downloads");
      },() -> {
        var ack=ack();if(ack==null||!"applied".equals(ack.get("status").getAsString()))return false;
        var sample=sample();if(!localOnly(sample,bodies)){firstStable=null;return false;}
        if(firstStable==null){firstStable=sample;stableSeq=sample.get("seq").getAsLong();return false;}
        if(sample.get("epoch").getAsLong()!=firstStable.get("epoch").getAsLong()){firstStable=sample;stableSeq=sample.get("seq").getAsLong();return false;}
        if(sample.get("seq").getAsLong()<=stableSeq||!sample.getAsJsonObject("policy").equals(firstStable.getAsJsonObject("policy")))return false;
        event("independent-effective-policy-and-exact-provider-stable",sample);if(bodies)enabledReady=true;else disabledReady=true;return true;
      },15_000,() -> {
        if(bodies)enabledReady=false;else disabledReady=false;
        var observed=sample();event("unestablished-effective-policy-snapshot",observed==null?"No ready coherent SDK sample":observed);
        var result=ack();event("independent-setup-ack",result==null?"No correlated setup acknowledgement":result);
        event("setup-obstacle","Raw typed setting was written by independent setup, but effective/provider prerequisite did not stabilize. Not a product policy-negative pass.");
      });
    }
    void open(Target target) {
      var file=LocalFileSystem.getInstance().refreshAndFindFileByNioFile(assets.resolve("flow.fsm"));require(file!=null,"Main must import owned Host.items.props");
      var document=FileDocumentManager.getInstance().getDocument(file);require(document!=null,"No actual fixture editor");String anchor="def "+target.node()+"("+target.key()+")";
      int offset=document.getText().indexOf(anchor);require(offset>=0,"Missing exact DSL anchor");offset+=anchor.indexOf(target.key());
      origin=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,file,offset),true);require(origin!=null,"No actual editor");origin.getCaretModel().moveToOffset(offset);origin.getSelectionModel().removeSelection();
    }
    void formalAction() {
      var action=ActionManager.getInstance().getAction("AnotherFSM.GoToImplementation");require(action!=null,"Missing registered implementation action");
      var context=DataManager.getInstance().getDataContext(origin.getContentComponent());require(CommonDataKeys.PROJECT.getData(context)==project&&CommonDataKeys.EDITOR.getData(context)==origin,"No actual editor/project action context");
      seen.clear();seen.addAll(Arrays.asList(NotificationsManager.getNotificationsManager().getNotificationsOfType(Notification.class,project)));
      event("registered-formal-action",Map.of("id","AnotherFSM.GoToImplementation","documentVersion",origin.getDocument().getModificationStamp(),"offset",origin.getCaretModel().getOffset()));
      action.actionPerformed(AnActionEvent.createFromAnAction(action,null,ActionPlaces.EDITOR_POPUP,context));
    }
    String explanation(Target target) {
      for(var notification:NotificationsManager.getNotificationsManager().getNotificationsOfType(Notification.class,project))if(!seen.contains(notification)) {
        String text=notification.getTitle()+" "+notification.getContent();if(text.contains(target.type())&&text.contains("Determined C# binding retained")&&text.contains("source match unverified"))return text;
      }return null;
    }
    boolean actualDeclaration(Target target) {
      var selected=FileEditorManager.getInstance(project).getSelectedTextEditor();if(selected==null||selected.getDocument()==origin.getDocument())return false;
      var document=selected.getDocument();int line=document.getLineNumber(selected.getCaretModel().getOffset());String text=document.getText().substring(document.getLineStartOffset(line),document.getLineEndOffset(line));
      if(!text.matches(".*\\bclass\\s+"+target.type()+"\\b.*"))return false;
      require(document.getText().contains("Decompiled with JetBrains decompiler"),"External editor lacks actual SDK-generated header; not verified by dispatch/path alone");
      var file=FileDocumentManager.getInstance().getFile(document);require(file!=null&&!file.getPath().replace('\\','/').endsWith("/SourceCases/candidates/mismatch.cs"),"Mismatching source candidate was opened as verified source");
      event("actual-generated-declaration-editor",Map.of("type",target.type(),"path",file.getPath(),"line",line+1,"decompilerHeader",true));return true;
    }
    void presentation(String name,Target target,boolean bodies,boolean broken) {
      step(name,() -> {
        reason=null;afterNotificationSequence=-1;
        if(!(bodies?enabledReady&&consentReady:disabledReady)){pending("Effective/provider/consent prerequisite unavailable; formal action not run.");return;}
        before=sample();require(localOnly(before,bodies)&&determined(before,target),"Policy/compiled identity changed before formal action");
        if(bodies)require(before.getAsJsonObject("policy").get("consent").getAsBoolean(),"No prior host legal consent");
        event("actual-target-policy-before-formal-action",before);open(target);formalAction();
      },() -> {
        if(steps.get(index).status.equals("pendingVerification"))return true;
        if(reason==null){reason=explanation(target);if(reason==null)return false;var sample=sample();if(sample==null)return false;afterNotificationSequence=sample.get("seq").getAsLong();event("actual-visible-product-label",reason);return false;}
        var after=sample();if(after==null||after.get("seq").getAsLong()<=afterNotificationSequence)return false;
        require(before.getAsJsonObject("policy").equals(after.getAsJsonObject("policy")),"Plugin changed policy across the actual operation");
        require(determined(after,target)&&target(before,target).get("identity").equals(target(after,target).get("identity")),"Determined binding lost/retargeted during presentation");
        require(reason.contains("assembly=")&&reason.contains("token=0x")&&reason.contains("consuming TFM="),"Missing retained metadata/consumer identity");
        require(!reason.toLowerCase(Locale.ROOT).matches("(?s).*(machine is offline|source access is unauthorized|private feed requires|source server returned 401).*"),"Product guessed an unobserved external cause");
        if(!bodies) {
          require(!reason.contains("Decompiled code")&&reason.contains("decompiler is not applicable"),"Effectively disabled decompiler was not refused by host policy");
          event("actual-policy-after-action",after);capture("source-disabled.png");return true;
        }
        boolean failure=reason.contains("decompiler presentation failed (")||reason.contains("decompiler produced no presentation");
        if(broken&&failure) {
          require(!reason.contains("legal notice has not already been accepted")&&!reason.contains("decompiler is not applicable"),"Consent/options refusal is not generation failure");
          event("actual-host-provider-presentation-failure-with-retained-metadata",Map.of("reason",reason,"snapshot",after));capture("source-provider-failure.png");
          event("observed-presentation-failure-scope","Actual host presentation failure retained confirmed metadata. Underlying engine/initialization/positioning cause is not inferred.");return true;
        }
        if(reason.contains("Decompiled code")||reason.contains("Metadata view")) {
          if(!actualDeclaration(target))return false;
          event("actual-policy-after-action",after);capture("source-"+target.fixture()+".png");
          if(broken) {
            String generated=FileEditorManager.getInstance(project).getSelectedTextEditor().getDocument().getText();
            var asset=prepared.getAsJsonObject("cases").getAsJsonObject("broken");var patch=prepared.getAsJsonObject("brokenPatch").getAsJsonObject("patch");
            require(generated.contains("// MVID: "+asset.get("mvid").getAsString().toUpperCase(Locale.ROOT)),"Generated failure view is not the prepared binary");
            int method=generated.indexOf("RenderProbe(");int end=method<0?-1:generated.indexOf('}',method);
            require(method>=0&&end>method&&generated.substring(method,end).contains("// ISSUE: unable to decompile the method."),"Malformed fixture did not produce an actual host method-decompilation failure");
            require(patch.get("metadataBytesUnchanged").getAsBoolean()&&"RenderProbe".equals(patch.get("method").getAsString()),"Failure oracle does not preserve type metadata");
            event("actual-host-method-decompilation-failure-retains-metadata",Map.of("method",generated.substring(method,end),"mvid",asset.get("mvid"),"oracle",patch,
                "scope","Method-level failure explicitly reported by host; not a whole-engine exception/cancellation claim"));
          }
          return true;
        }
        if(!reason.contains("navigation dispatched")) {
          pending("Determined binding and metadata/unverified retained, but no actual generated declaration editor was opened. Not allowed-navigation UI success or engine-failure proof: "+reason);return true;
        }
        return false;
      });
    }
    void capture(String filename) {
      try {var frame=com.intellij.openapi.wm.WindowManager.getInstance().getFrame(project);require(frame!=null&&frame.isShowing(),"No actual visible Rider frame");
        ImageIO.write(new Robot().createScreenCapture(frame.getBounds()),"png",output.resolve(filename).toFile());event("actual-window-screenshot",filename);
      }catch(Exception error){throw new IllegalStateException(error);}
    }
    void service(Target target) {
      step("Controlled source-service availability: "+target.fixture(),() -> {
        int port=prepared.get(target.fixture().equals("offline")?"offlinePort":"port").getAsInt();
        serviceProbe=AppExecutorUtil.getAppExecutorService().submit(() -> probe(target.fixture(),port));
      },() -> {
        if(!serviceProbe.isDone())return false;
        try {var proof=serviceProbe.get();serviceEvidence.put(target.fixture(),proof);event("controlled-loopback-availability-only",proof);
          if(!proof.get("established").getAsBoolean())pending("Controlled local source-service failure not established: "+proof);return true;
        }catch(Exception error){throw new IllegalStateException(error);}
      });
    }
    JsonObject probe(String fixture,int port) {
      var evidence=new JsonObject();evidence.addProperty("fixture",fixture);evidence.addProperty("host","127.0.0.1");evidence.addProperty("port",port);evidence.addProperty("sampledUtc",Instant.now().toString());
      evidence.addProperty("scope","source-service availability only; no whole-machine offline/private credentials/host downloader claim");
      try {
        if(fixture.equals("offline")) {
          try(var socket=new Socket()){socket.connect(new InetSocketAddress("127.0.0.1",port),1500);evidence.addProperty("established",false);evidence.addProperty("result","endpoint unexpectedly reachable");}
          catch(ConnectException refused){evidence.addProperty("established",true);evidence.addProperty("result","loopback connection refused");}
        } else {
          String identity=prepared.get("preparationId").getAsString();
          var health=(HttpURLConnection)new URI("http://127.0.0.1:"+port+"/health").toURL().openConnection(Proxy.NO_PROXY);
          health.setAuthenticator(new Authenticator() {});health.setUseCaches(false);
          health.setInstanceFollowRedirects(false);health.setConnectTimeout(2000);health.setReadTimeout(2000);
          boolean owned;
          try {owned=health.getResponseCode()==200&&identity.equals(health.getHeaderField("X-AnotherFSM-Source-Fixture"));}
          finally {health.disconnect();}
          if(!owned){evidence.addProperty("established",false);evidence.addProperty("result","Not the prepared owned loopback fixture; no 401 probe sent");return evidence;}
          var connection=(HttpURLConnection)new URI("http://127.0.0.1:"+port+"/unauthorized/unauthorized.cs").toURL().openConnection(Proxy.NO_PROXY);
          connection.setAuthenticator(new Authenticator() {});connection.setUseCaches(false);
          connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(2000);connection.setReadTimeout(2000);
          try {int status=connection.getResponseCode();evidence.addProperty("status",status);evidence.addProperty("authorizationSent",false);
            evidence.addProperty("ownedPreparationId",identity);evidence.addProperty("established",status==401&&identity.equals(connection.getHeaderField("X-AnotherFSM-Source-Fixture")));}
          finally {connection.disconnect();}
        }
      }catch(Exception error){evidence.addProperty("established",false);evidence.addProperty("observedError",error.getClass().getSimpleName());}
      return evidence;
    }
    boolean consentWitness() {
      var witness=json(output.resolve("source-consent-witness.json"));if(witness==null)return false;
      require(runId.equals(witness.get("runId").getAsString())&&commandId.equals(witness.get("commandId").getAsString())&&witness.get("frontendPid").getAsLong()==ProcessHandle.current().pid(),"Legal UI witness correlation mismatch");
      require(witness.get("clickedYes").getAsBoolean()&&witness.get("ownedFixtureOnly").getAsBoolean()&&!witness.get("dialogCaption").getAsString().isBlank(),"Main did not witness explicit own-fixture legal approval");
      String filename=witness.get("dialogScreenshot").getAsString();require(Path.of(filename).getNameCount()==1&&!Path.of(filename).isAbsolute()
          &&output.equals(output.resolve(filename).normalize().getParent()),"Screenshot must be a local output filename");
      try {require(sha(Files.readAllBytes(output.resolve(filename))).equals(witness.get("dialogScreenshotSha256").getAsString()),"Actual main-observed legal-dialog screenshot digest mismatch");}
      catch(java.io.IOException error){throw new IllegalStateException(error);}
      event("main-supplied-real-legal-UI-witness",witness);return true;
    }
    boolean scriptHostReady(JsonObject sample) {
      if(warmReply!=null) {
        if(Set.of("analyzing","stale").contains(warmReply.get("status").getAsString())
            ||warmReply.get("contextVersion").getAsLong()!=sample.get("epoch").getAsLong()) {
          warmReply=null;warmSent=false;warmSequence=-1;if(warmRequest!=null)Disposer.dispose(warmRequest);return false;
        }
        if(warmSequence<0){warmSequence=sample.get("seq").getAsLong();return false;}
        return sample.get("seq").getAsLong()>warmSequence;
      }
      if(!warmSent) {
        warmSent=true;open(TARGETS.get(0));var document=origin.getDocument();var request=new JsonObject();
        request.addProperty("action","resolve");request.addProperty("implementation",true);request.addProperty("path",FileDocumentManager.getInstance().getFile(document).getPath());
        request.addProperty("text",document.getText());request.addProperty("offset",origin.getCaretModel().getOffset());request.addProperty("documentVersion",document.getModificationStamp());request.addProperty("requestId",UUID.randomUUID().toString());
        warmRequest=BackendConnection.get(project).query(request.toString(),raw -> {
          var parsed=JsonParser.parseString(raw);if(parsed.isJsonObject()){warmReply=parsed.getAsJsonObject();event("script-document-readiness-only-not-presentation",warmReply);}else warmSent=false;
        },error -> {warmSent=false;event("script-readiness-retry",error.getClass().getSimpleName());});
      }
      return false;
    }
    void specify() {
      step("Verify live isolation/exact prelaunch backup/actual compiled source-case host bindings",() -> {},() -> {
        if(!authorize())return false;var sample=sample();if(sample==null)return false;
        return TARGETS.stream().allMatch(target -> determined(sample,target))&&scriptHostReady(sample);
      },90_000,() -> {throw new IllegalStateException("Setup pending: main must prepare/import SourceCases and exact prelaunch backup; no policy setup was authorized without them");});
      policySetup(false);
      presentation("Effective decompiler disabled + PDB Download/UseSymbolFiles/SourceLink=false survive formal action",TARGETS.get(0),false,false);
      policySetup(true);
      step("Host-approved decompiler consent: existing approval or main-witnessed real legal notice",() -> {
        if(!enabledReady){pending("Effective decompiler setup not established; no legal request issued.");return;}
        command("request-consent");var request=new JsonObject();request.addProperty("runId",runId);request.addProperty("commandId",commandId);request.addProperty("frontendPid",ProcessHandle.current().pid());
        request.addProperty("instruction","Observe the real host legal dialog, save its real screenshot under this output, explicitly click Yes only for owned test fixtures; write source-consent-witness.json with runId/commandId/frontendPid/clickedYes/ownedFixtureOnly/dialogCaption/dialogScreenshot/dialogScreenshotSha256. No typed consent writes.");write("source-consent-request.json",request);
      },() -> {
        if(!enabledReady)return true;var ack=ack();if(ack==null||!"noticeReturned".equals(ack.get("status").getAsString()))return false;
        if(!ack.get("checkAndAskUserResult").getAsBoolean()){pending("Actual legal notice was not accepted; allowed operations will not run.");return true;}
        var sample=sample();if(!localOnly(sample,true)||!sample.getAsJsonObject("policy").get("consent").getAsBoolean())return false;
        consentReady=true;
        if(ack.get("alreadyAccepted").getAsBoolean()){event("existing-host-consent-no-new-UI-acceptance-claimed",ack);return true;}
        if(!consentWitness())return false;event("actual-legal-api-return-and-observed-consent",ack);return true;
      },600_000,() -> {var sample=sample();consentReady=sample!=null&&sample.getAsJsonObject("policy").get("consent").getAsBoolean();event("legal-ui-obstacle","No completed real legal-dialog witness before deadline. Main must handle any still-open modal UI; no automatic approval/cancellation is performed.");});
      service(TARGETS.get(0));presentation("Actual allowed presentation while the owned loopback source service refuses connections",TARGETS.get(0),true,false);
      service(TARGETS.get(1));presentation("Actual allowed presentation with controlled local HTTP401 source evidence",TARGETS.get(1),true,false);
      presentation("SourceCSPath candidate/PDB checksum differs: actual presentation remains explicitly unverified",TARGETS.get(2),true,false);
      presentation("No CodeView/PDB/embedded source: actual presentation does not invent verified source",TARGETS.get(3),true,false);
      presentation("Actual host method-decompilation failure on invalid IL retains confirmed type metadata",TARGETS.get(4),true,true);
    }
    void report(String status,String error) {
      try {Files.createDirectories(output);var report=new JsonObject();report.addProperty("status",status);report.addProperty("error",error);report.addProperty("runId",runId);report.addProperty("frontendPid",ProcessHandle.current().pid());report.addProperty("backendPid",backendPid);report.addProperty("elapsedMs",ms(started));report.addProperty("step",index);
        var coverage=new JsonArray();for(var step:steps){var item=new JsonObject();item.addProperty("name",step.name);item.addProperty("status",step.status);item.addProperty("detail",step.detail);coverage.add(item);}report.add("coverage",coverage);
        report.add("observations",gson.toJsonTree(events));report.add("pendingVerification",gson.toJsonTree(gaps));report.addProperty("restoration","Main must close sandbox and run exact restore-settings; this report does NOT claim restoration.");
        String marker=System.getenv("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");var sentinel=new JsonObject();sentinel.addProperty("configured",marker!=null&&!marker.isBlank());
        sentinel.addProperty("exists",marker!=null&&!marker.isBlank()&&Files.exists(Path.of(marker)));sentinel.addProperty("scope","instrumented fixture paths only; absence is not universal execution/network proof");report.add("sentinel",sentinel);
        report.add("outOfScope",gson.toJsonTree(List.of("#35 documentation/completion","#36 performance")));Files.writeString(output.resolve("source-boundary-acceptance.json"),gson.toJson(report));
      }catch(Exception failure){failure.printStackTrace();}
    }
    void finish(boolean successful,String error) {
      if(done)return;done=true;if(timer!=null)timer.cancel(false);if(serviceProbe!=null)serviceProbe.cancel(true);if(warmRequest!=null)Disposer.dispose(warmRequest);
      boolean pending=!successful&&index<steps.size()&&steps.get(index).status.equals("pendingVerification");
      if(!successful&&!pending&&index<steps.size()){steps.get(index).status="failed";steps.get(index).detail=error;}
      try {
        // Stop only our command/authorization. No settings writes at teardown; main restores exact layers after shutdown.
        for(var filename:List.of("source-policy-command.json","source-policy-authorization.json")) {
          var value=json(output.resolve(filename));if(value!=null&&runId.equals(value.get("runId").getAsString()))Files.deleteIfExists(output.resolve(filename));
        }sentinel();
      }catch(Throwable cleanup){successful=false;pending=false;error=String.valueOf(error)+"; cleanup="+cleanup;}
      report(!successful?(pending?"pendingVerification":"failed"):"partial",error);if(successful&&continuation!=null)continuation.run();
    }
  }
}
