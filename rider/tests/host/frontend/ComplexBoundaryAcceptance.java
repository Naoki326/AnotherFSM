package anotherfsm.acceptance;

import anotherfsm.BackendConnection;
import com.google.gson.*;
import com.intellij.ide.DataManager;
import com.intellij.notification.*;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.actionSystem.*;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.command.WriteCommandAction;
import com.intellij.openapi.editor.*;
import com.intellij.openapi.fileEditor.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.util.Disposer;
import com.intellij.openapi.util.ActionCallback;
import com.intellij.openapi.vfs.*;
import com.intellij.openapi.wm.ToolWindowManager;
import com.intellij.ui.content.Content;
import com.intellij.usages.Usage;
import com.intellij.usages.UsageView;
import com.intellij.usages.impl.UsageViewImpl;
import com.intellij.util.concurrency.AppExecutorUtil;
import com.jetbrains.rd.ide.model.RdFindUsagesSession;
import com.jetbrains.rdclient.usages.FrontendUsageTarget;
import com.jetbrains.rider.model.RdProjectDescriptor;
import com.jetbrains.rider.model.RdUnloadProjectDescriptor;
import com.jetbrains.rider.model.RdUnloadProjectState;
import java.awt.*;
import java.awt.event.*;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.List;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.*;
import javax.swing.*;

/** Independent #34 boundary journey; deliberately NOT a StartupActivity.
 * Integration: explicitly prepare and open tests/fixtures/boundary/Boundary.sln in the verified
 * sandbox, then call start(project, output, boundaryRoot, continuation). Never run concurrently
 * with ComplexHostAcceptance. The continuation means no failed assertion/cleanup, NOT full AC pass:
 * inspect complex-boundary-acceptance.json, whose terminal status can be partial/pendingVerification.
 * No IDE launch/build/restore or policy writes. Only our new Host/BoundaryHost.csproj is edited,
 * through a real editor and saved to trigger Rider's project reevaluation. Exact original bytes
 * are restored, with concurrent-edit guards. Observed module/reference/TFM changes, not save/action
 * dispatch or a document epoch bump, establish reload. No private production fields are read.
 */
public final class ComplexBoundaryAcceptance {
  private ComplexBoundaryAcceptance() {}
  public static void start(Project project,Path output,Path root,Runnable onSuccess) {
    ApplicationManager.getApplication().invokeLater(() -> new Journey(project,output,root,onSuccess).start());
  }
  private static final String IMPL="AnotherFSM.GoToImplementation", REFS="AnotherFSM.FindReferences";
  private static final class Step {
    final String name;final Runnable enter;final BooleanSupplier verify;final long timeout;final boolean setup;
    String status="notRun",detail;long begun;
    Step(String name,Runnable enter,BooleanSupplier verify,long timeout,boolean setup) {
      this.name=name;this.enter=enter;this.verify=verify;this.timeout=timeout;this.setup=setup;
    }
  }
  private static final class StableHost {
    long epoch=-1,sequence=-1;int count;
    boolean observe(JsonObject sample) {
      if(sample==null){count=0;return false;}
      long next=sample.get("seq").getAsLong(),version=sample.get("epoch").getAsLong();
      if(next==sequence)return count>=4;
      sequence=next;count=version==epoch?count+1:1;epoch=version;return count>=4;
    }
    void reset(){count=0;epoch=-1;sequence=-1;}
  }
  private static final class Journey {
    final Project project;final Path output,root;final Runnable continuation;
    final String runId=UUID.randomUUID().toString();final Gson gson=new GsonBuilder().setPrettyPrinting().create();
    final List<Step> steps=new ArrayList<>();final List<Object> events=new ArrayList<>();
    final List<String> pending=new ArrayList<>();final List<Content> ownedPages=new ArrayList<>();
    final Set<Notification> seen=Collections.newSetFromMap(new IdentityHashMap<>());
    final Set<Content> pageBaseline=Collections.newSetFromMap(new IdentityHashMap<>());
    final Set<Integer> descriptorBaseline=new HashSet<>();final AtomicBoolean queued=new AtomicBoolean();
    final long started=System.nanoTime();ScheduledFuture<?> timer;Disposable query;int index;boolean done,isolation;
    Editor editor;Document projectDocument;String originalProject,ownedProject,lastSavedReadback;
    boolean finishing,cleanupSuccess,cleanupIncomplete,restoreRequested;String cleanupError;long cleanupStarted,lastProjectSave;
    byte[] originalBytes;Content dsl,nativePage;int nativeId;JsonArray nativeRows;List<String> dslRows;
    final Set<Window> priorWindows=Collections.newSetFromMap(new IdentityHashMap<>());
    JsonObject before,policyBefore,oldRequest,oldBinding,reply;long baselineSequence,backendPid=-1;
    long heldEpoch,policyResponseSequence;String heldSession,policyReason;boolean querySent,policyEstablished;int requestGeneration;

    Journey(Project project,Path output,Path root,Runnable continuation) {
      this.project=project;this.output=output.toAbsolutePath().normalize();this.root=root.toAbsolutePath().normalize();this.continuation=continuation;
      pending.add("Real external generation failure/cancellation: no actual cancellable generation task was entered/cancelled; not covered by consent refusal or observer read cancellation.");
      pending.add("Offline/private-feed 401/source-version mismatch/insufficient symbols: no external source-provider operation or authorized source fixture; no offline/private/permission diagnosis inferred. No source downloads/authentication attempted.");
      pending.add("Untrusted solution refusal: not inferred from a loose file or unload. Native unloaded state is tested separately; declining a real trust prompt still needs its own visible host evidence.");
      specify();
    }
    void require(boolean value,String message) {if(!value)throw new IllegalStateException(message);}
    long ms(long then) {return (System.nanoTime()-then)/1_000_000;}
    void event(String kind,Object value) {events.add(Map.of("step",index,"elapsedMs",ms(started),"kind",kind,"value",value));}
    void step(String name,Runnable enter,BooleanSupplier verify) {steps.add(new Step(name,enter,verify,25_000,false));}
    void setup(String name,Runnable enter,BooleanSupplier verify) {steps.add(new Step(name,enter,verify,90_000,true));}
    void once(String name,Runnable body) {step(name,body,() -> true);}
    void pending(String message) {var step=steps.get(index);step.status="pendingVerification";step.detail=message;event("pending",message);}
    void start() {
      report("running",null);
      timer=AppExecutorUtil.getAppScheduledExecutorService().scheduleWithFixedDelay(() -> {
        if(done||!queued.compareAndSet(false,true))return;
        ApplicationManager.getApplication().invokeLater(() -> {
          try {tick();}catch(Throwable error){finish(false,error.toString());}finally{queued.set(false);}
        });
      },0,250,TimeUnit.MILLISECONDS);
    }
    void tick() {
      if(done)return;if(finishing){finishCleanup();return;}require(!project.isDisposed(),"Boundary project disposed");sentinel();
      if(index==steps.size()){finish(true,null);return;}
      require(ms(started)<900_000,"Boundary journey total deadline exceeded");
      var step=steps.get(index);
      if(step.status.equals("notRun")) {step.status="running";step.begun=System.nanoTime();event("begin",step.name);step.enter.run();report("running",null);return;}
      if(step.verify.getAsBoolean()) {if(step.status.equals("running"))step.status="passed";event(step.status,step.name);index++;report("running",null);}
      else if(ms(step.begun)>step.timeout) {
        if(step.setup)pending("Real host setup/reload not established before deadline; downstream assertions not run. No zero-result pass.");
        throw new IllegalStateException("Deadline: "+step.name);
      }
    }
    VirtualFile file(String relative) {
      var result=LocalFileSystem.getInstance().refreshAndFindFileByNioFile(root.resolve(relative));
      require(result!=null,"Missing controlled fixture: "+relative);return result;
    }
    Document document(String relative) {var result=FileDocumentManager.getInstance().getDocument(file(relative));require(result!=null,"No real document: "+relative);return result;}
    int offset(Document document,String anchor,String token) {
      String text=document.getText();int start=text.indexOf(anchor);
      require(start>=0&&text.indexOf(anchor,start+1)<0&&anchor.contains(token),"Missing/nonunique anchor: "+anchor);return start+anchor.indexOf(token);
    }
    void open(String relative,String anchor,String token) {
      var document=document(relative);editor=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,file(relative),offset(document,anchor,token)),true);
      require(editor!=null,"No actual editor");editor.getSelectionModel().removeSelection();editor.getCaretModel().moveToOffset(offset(document,anchor,token));
    }
    void stationary() {open("Host/flow.fsm","def BinaryEntry(HostDllKey)","BinaryEntry");}
    boolean stationaryNow() {
      var current=FileEditorManager.getInstance(project).getSelectedTextEditor();
      return current!=null&&current.getDocument()==document("Host/flow.fsm")
          &&current.getCaretModel().getOffset()==offset(current.getDocument(),"def BinaryEntry(HostDllKey)","BinaryEntry")
          &&!current.getSelectionModel().hasSelection();
    }
    void action(String id) {
      var action=ActionManager.getInstance().getAction(id);require(action!=null,"Missing registered action: "+id);
      var context=DataManager.getInstance().getDataContext(editor.getContentComponent());
      require(CommonDataKeys.PROJECT.getData(context)==project&&CommonDataKeys.EDITOR.getData(context)==editor,"Not an actual editor/project action context");
      event("registered-action",Map.of("id",id,"offset",editor.getCaretModel().getOffset()));
      action.actionPerformed(AnActionEvent.createFromAnAction(action,null,ActionPlaces.EDITOR_POPUP,context));
    }
    List<Component> tree(Component component) {
      var result=new ArrayList<Component>();result.add(component);
      if(component instanceof Container parent)for(var child:parent.getComponents())result.addAll(tree(child));return result;
    }
    Content[] pages(String id) {var window=ToolWindowManager.getInstance(project).getToolWindow(id);return window==null?new Content[0]:window.getContentManager().getContents();}
    JList<?> list(Content page) {
      if(page!=null)for(var component:tree(page.getComponent()))if(component instanceof JList<?> list&&"AnotherFSM.references".equals(list.getName()))return list;return null;
    }
    List<String> rows(Content page) {
      var list=list(page);var result=new ArrayList<String>();if(list!=null)for(int i=0;i<list.getModel().getSize();i++)result.add(String.valueOf(list.getModel().getElementAt(i)));return result;
    }
    boolean expired(Content page) {return page!=null&&page.getDisplayName().startsWith("结果已过期");}
    void baselineNotifications() {seen.clear();seen.addAll(Arrays.asList(NotificationsManager.getNotificationsManager().getNotificationsOfType(Notification.class,project)));}
    String notification(Predicate<String> predicate) {
      for(var item:NotificationsManager.getNotificationsManager().getNotificationsOfType(Notification.class,project))if(!seen.contains(item)) {
        String text=item.getTitle()+" "+item.getContent();if(predicate.test(text)){event("visible-product-reason",text);return text;}
      }return null;
    }
    JsonObject json(Path path) {
      try {return Files.isRegularFile(path)?JsonParser.parseString(Files.readString(path)).getAsJsonObject():null;}
      catch(java.io.IOException|JsonParseException transientRead){return null;}
    }
    JsonObject sample() {
      var sample=json(output.resolve("complex-boundary-observation.json"));
      if(sample==null||!sample.has("runId")||!runId.equals(sample.get("runId").getAsString()))return null;
      if(sample.has("error"))throw new IllegalStateException("Boundary SDK observer error: "+sample.get("error"));
      if(backendPid<0)backendPid=sample.get("backendPid").getAsLong();
      require(backendPid==sample.get("backendPid").getAsLong(),"Backend process replaced; no mixed-generation pass");
      require(!sample.getAsJsonObject("sentinel").get("exists").getAsBoolean(),"Backend execution marker exists");
      return sample.get("ready").getAsBoolean()&&sample.get("coherent").getAsBoolean()?sample:null;
    }
    JsonObject module(JsonObject sample) {return sample!=null&&sample.getAsJsonArray("modules").size()==1?sample.getAsJsonArray("modules").get(0).getAsJsonObject():null;}
    JsonObject binding(JsonObject sample,String key) {
      var module=module(sample);if(module!=null)for(var item:module.getAsJsonArray("bindings"))if(key.equals(item.getAsJsonObject().get("key").getAsString()))return item.getAsJsonObject();return null;
    }
    boolean bound(JsonObject sample,String key,String state,String type) {
      var binding=binding(sample,key);return binding!=null&&state.equals(binding.get("state").getAsString())
          &&(type==null||binding.getAsJsonArray("candidates").toString().contains(type));
    }
    boolean sandbox() {
      if(isolation)return true;
      require(Files.isRegularFile(root.resolve("Boundary.sln")),"Use NEW boundary fixture, not Complex root");
      require(project.getBasePath()!=null&&Path.of(project.getBasePath()).toAbsolutePath().normalize().equals(root),"Wrong actual open project root");
      var rider=root.getParent().getParent().getParent();var box=rider.resolve(".sandbox");
      var values=new LinkedHashMap<String,String>();
      for(var name:List.of("config","system","plugins")) {
        String property="idea."+name+".path",value=System.getProperty(property);values.put(property,String.valueOf(value));
        require(value!=null&&Path.of(value).toAbsolutePath().normalize().equals(box.resolve(name)),"Not isolated sandbox: "+property);
      }
      String log=System.getProperty("idea.log.path");require(log!=null&&Path.of(log).toAbsolutePath().normalize().equals(rider.getParent().resolve(".scratch/issue-32/sandbox-ide")),"Wrong isolated log path");values.put("idea.log.path",log);
      for(var property:List.of("idea.config.import.enabled","idea.settings.sync.enabled","idea.initially.ask.config")) {
        String value=System.getProperty(property);require("false".equals(value),"Import/sync not disabled: "+property);values.put(property,value);
      }
      var launcher=json(box.resolve("launcher.json"));if(launcher==null||!launcher.has("windowsPid")||launcher.get("windowsPid").getAsLong()!=ProcessHandle.current().pid())return false;
      require(Path.of(launcher.get("fixture").getAsString()).toAbsolutePath().normalize().equals(root.resolve("Boundary.sln")),"Launcher must explicitly open Boundary.sln");
      require(Path.of(launcher.get("cwd").getAsString()).toAbsolutePath().normalize().equals(box),"Wrong launcher cwd");
      boolean outputAuthorized=false;for(var value:launcher.getAsJsonArray("expandedArguments")) {
        String arg=value.getAsString();if(arg.startsWith("-Danotherfsm.acceptance.dir="))outputAuthorized=Path.of(arg.substring(arg.indexOf('=')+1)).toAbsolutePath().normalize().equals(output);
      }
      require(outputAuthorized,"Output not authorized by actual launcher");
      try {
        String arg=launcher.getAsJsonArray("command").get(1).getAsString();require(arg.startsWith("@"),"No launcher argfile");
        String hash=HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(Path.of(arg.substring(1)))));
        require(hash.equals(launcher.get("argfileSha256").getAsString())&&Files.isRegularFile(box.resolve("prepared.json")),"Unverified launcher/prepared sandbox");
        event("verified-isolation",Map.of("properties",values,"pid",ProcessHandle.current().pid(),"argfileSha256",hash,"root",root.toString()));
        Files.createDirectories(output);var command=new JsonObject();command.addProperty("operation","observe-only");command.addProperty("runId",runId);command.addProperty("root",root.toString());
        Files.writeString(output.resolve("complex-boundary-observe.json.tmp"),gson.toJson(command));
        Files.move(output.resolve("complex-boundary-observe.json.tmp"),output.resolve("complex-boundary-observe.json"),StandardCopyOption.REPLACE_EXISTING);
        isolation=true;return true;
      }catch(Exception error){throw new IllegalStateException(error);}
    }
    void sentinel() {
      String marker=System.getenv("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");
      if(marker!=null&&!marker.isBlank()){require(Path.of(marker).isAbsolute(),"Marker must be absolute");require(!Files.exists(Path.of(marker)),"Business execution marker exists; never deleted");}
    }
    void query(JsonObject request,Consumer<JsonObject> accept) {
      int generation=++requestGeneration;query=BackendConnection.get(project).query(request.toString(),raw -> {
        if(done||generation!=requestGeneration)return;
        try {var result=JsonParser.parseString(raw).getAsJsonObject();require(request.get("requestId").equals(result.get("requestId")),"SDK request identity mismatch");accept.accept(result);}
        catch(Throwable error){finish(false,error.toString());}
      },error -> {if(!done&&generation==requestGeneration)finish(false,"SDK query failed: "+error);});
    }
    JsonObject resolveRequest(String key) {
      var document=document("Host/flow.fsm");String anchor=switch(key) {
        case "BoundaryReferenceKey" -> "def ReferenceEntry(BoundaryReferenceKey)";
        case "BoundaryProjectKey" -> "def ProjectEntry(BoundaryProjectKey)";
        default -> "def FrameworkEntry(BoundaryFrameworkKey)";
      };
      var request=new JsonObject();request.addProperty("action","resolve");request.addProperty("implementation",true);
      request.addProperty("path",file("Host/flow.fsm").getPath());request.addProperty("text",document.getText());request.addProperty("offset",offset(document,anchor,key));
      request.addProperty("documentVersion",document.getModificationStamp());request.addProperty("requestId",UUID.randomUUID().toString());return request;
    }
    void holdBinding(String key) {
      setup("hold determined public binding before reload: "+key,() -> {reply=null;querySent=false;},() -> {
        if(reply==null){if(!querySent){querySent=true;oldRequest=resolveRequest(key);query(oldRequest,result -> {reply=result;querySent=false;});}return false;}
        if(Set.of("analyzing","stale").contains(reply.get("status").getAsString())){reply=null;return false;}
        require("type".equals(reply.get("status").getAsString()),"Expected actual determined binding before reload: "+reply);
        oldBinding=reply.deepCopy();event("public-binding-before-reload-not-UI",oldBinding);return true;
      });
    }
    void lists(String node,String key,String source,String type) {
      final int[] baselineReruns={0},rerunOldRow={-1};
      step("retain real DSL list before reload: "+key,() -> {
        pageBaseline.clear();pageBaseline.addAll(Arrays.asList(pages("AnotherFSM")));
        open("Host/flow.fsm","def "+node+"("+key+")",node);action(REFS);
      },() -> {
        for(var page:pages("AnotherFSM"))if(!pageBaseline.contains(page)&&!expired(page)&&rows(page).size()==2) {
          require(rows(page).stream().allMatch(row -> row.contains("/Host/flow.fsm")),"Unexpected actual DSL result file");
          dsl=page;dslRows=rows(page);ownedPages.add(page);event("actual-DSL-model",dslRows);return true;
        }return false;
      });
      step("retain completed native FindUsages before reload: "+key,() -> {
        var observation=json(output.resolve("native-observation.json"));require(observation!=null,"HostObserver must be installed");
        require(observation.get("backendPid").getAsLong()==backendPid,"Wrong native observer backend");baselineSequence=observation.get("seq").getAsLong();descriptorBaseline.clear();
        for(var item:observation.getAsJsonArray("descriptors"))descriptorBaseline.add(item.getAsJsonObject().get("id").getAsInt());
        open(source,"class "+type,type);action("FindUsages");
      },() -> {
        var window=ToolWindowManager.getInstance(project).getToolWindow("Find");if(window==null)return false;
        var page=window.getContentManager().getSelectedContent();if(page==null||!page.getDisplayName().contains(type))return false;
        var view=UsageView.USAGE_VIEW_KEY.getData(DataManager.getInstance().getDataContext(page.getComponent()));if(!(view instanceof UsageViewImpl actual))return false;
        var sessions=Collections.newSetFromMap(new IdentityHashMap<RdFindUsagesSession,Boolean>());
        for(var target:actual.getTargets())if(target instanceof FrontendUsageTarget frontend)sessions.add(frontend.getSession());
        if(sessions.size()!=1||!Boolean.TRUE.equals(sessions.iterator().next().isSearchComplete().getValueOrNull()))return false;
        var observation=json(output.resolve("native-observation.json"));if(observation==null||observation.get("seq").getAsLong()<=baselineSequence)return false;
        require(observation.get("backendPid").getAsLong()==backendPid,"Wrong native observer backend");
        for(var item:observation.getAsJsonArray("descriptors")) {
          var descriptor=item.getAsJsonObject();if(descriptorBaseline.contains(descriptor.get("id").getAsInt())||!descriptor.get("title").getAsString().contains(type)
              ||descriptor.get("busy").getAsBoolean()||descriptor.get("updating").getAsBoolean())continue;
          var matches=new JsonArray();for(var entry:descriptor.getAsJsonArray("items")) {
            var row=entry.getAsJsonObject();if(row.has("runtimeType")&&!row.get("runtimeType").isJsonNull()&&"AnotherFSM.Rider.FsmOccurrence".equals(row.get("runtimeType").getAsString()))matches.add(row);
          }
          if(matches.size()!=1)continue;var row=matches.get(0).getAsJsonObject();
          require(key.equals(row.get("currentSlice").getAsString())&&row.get("file").getAsString().endsWith("/Host/flow.fsm"),"Wrong fresh native exact key/range: "+row);
          if(row.get("id").getAsInt()==rerunOldRow[0])return false;
          if(!row.get("valid").getAsBoolean()) {
            require(baselineReruns[0]++<3,"Native baseline repeatedly expires before project mutation: "+row);
            rerunOldRow[0]=row.get("id").getAsInt();
            var context=DataManager.getInstance().getDataContext(page.getComponent());
            var rerun=ActionManager.getInstance().getAction("UsageView.Rerun");require(rerun!=null,"Missing actual native Rerun action");
            event("explicit-native-baseline-rerun-after-host-warmup",Map.of("oldRow",row,"attempt",baselineReruns[0]));
            rerun.actionPerformed(AnActionEvent.createFromAnAction(rerun,null,ActionPlaces.USAGE_VIEW_TOOLBAR,context));return false;
          }
          nativePage=page;nativePage.setPinned(true);ownedPages.add(page);nativeId=descriptor.get("id").getAsInt();nativeRows=matches.deepCopy();
          event("completed-real-native-list",Map.of("id",nativeId,"rows",nativeRows));return true;
        }return false;
      });
      step("refresh actual DSL baseline after native host indexing: "+key,() -> {
        var window=ToolWindowManager.getInstance(project).getToolWindow("AnotherFSM");window.getContentManager().setSelectedContent(dsl);window.show(null);
        var buttons=tree(dsl.getComponent()).stream().filter(component -> component instanceof JButton button&&"刷新".equals(button.getText())).toList();
        require(buttons.size()==1,"Missing unique actual DSL Refresh button");((JButton)buttons.get(0)).doClick();
      },() -> {if(expired(dsl)||rows(dsl).size()!=2||!nativeRowsHaveValidity(true))return false;dslRows=rows(dsl);return true;});
    }
    void saveProject(UnaryOperator<String> change) {
      try {
        if(projectDocument==null) {
          projectDocument=document("Host/BoundaryHost.csproj");require(!FileDocumentManager.getInstance().isDocumentUnsaved(projectDocument),"Pre-existing unsaved project; refusing edit");
          originalProject=projectDocument.getText();originalBytes=Files.readAllBytes(root.resolve("Host/BoundaryHost.csproj"));ownedProject=originalProject;
        }
        require(projectDocument.getText().equals(ownedProject)&&Files.readString(root.resolve("Host/BoundaryHost.csproj")).replace("\r\n","\n").equals(ownedProject),"Concurrent project edit; refusing overwrite");
        String next=change.apply(originalProject);require(!next.equals(ownedProject),"No-op project edit");
        open("Host/BoundaryHost.csproj","<TargetFramework>","<TargetFramework>");
        WriteCommandAction.runWriteCommandAction(project,() -> projectDocument.setText(next));ownedProject=next;
        lastProjectSave=System.nanoTime();FileDocumentManager.getInstance().saveDocument(projectDocument);
        event("project-editor-save-requested-await-disk", Map.of("path",root.resolve("Host/BoundaryHost.csproj").toString(),"text",next,"automaticHostReloadRequired",true));
      }catch(java.io.IOException error){throw new IllegalStateException(error);}
    }
    boolean projectSaved() {
      try {
        String disk=Files.readString(root.resolve("Host/BoundaryHost.csproj")).replace("\r\n","\n");
        if(!disk.equals(ownedProject))return false;
        if(!disk.equals(lastSavedReadback)) {
          event("actual-project-save-readback",Map.of("path",root.resolve("Host/BoundaryHost.csproj").toString(),"text",disk));lastSavedReadback=disk;
        }
        return true;
      }catch(java.io.IOException pendingSave){return false;}
    }
    void reload(String name,String key,String node,String source,String type,UnaryOperator<String> mutation,Predicate<JsonObject> established) {
      var warm=new StableHost();
      setup(name+": open actual baseline editors and wait stable host",() -> {
        open(source,"class "+type,type);open("Host/flow.fsm","def "+node+"("+key+")",node);warm.reset();
      },() -> {
        var sample=sample();if(module(sample)==null||!bound(sample,key,"Determined",type)){warm.reset();return false;}
        return warm.observe(sample);
      });
      lists(node,key,source,type);holdBinding(key);
      final ActionCallback[] reloadAction={null};final long[] reloadSample={-1};final int[] reloadAttempts={0};
      setup(name+": real saved project reevaluation",() -> {
        before=sample();require(module(before)!=null,"No ready baseline module");heldEpoch=before.get("epoch").getAsLong();heldSession=before.get("session").getAsString();
        require(oldBinding.get("contextVersion").getAsLong()==heldEpoch&&oldBinding.get("session").getAsString().equals(heldSession),"Held binding already stale BEFORE project mutation; rerun, no reload proof");
        require(!expired(dsl)&&!expired(nativePage)&&nativeRowsHaveValidity(true),"Displayed results already expired BEFORE project mutation; no reload proof");
        saveProject(mutation);
      },() -> {
        if(!projectSaved())return false;
        var sample=sample();boolean changed=module(sample)!=null&&sample.get("structureVersion").getAsLong()>before.get("structureVersion").getAsLong()&&established.test(sample);
        if(!changed) {
          if(reloadAction[0]!=null&&reloadAction[0].isRejected()&&sample!=null&&sample.get("seq").getAsLong()>=reloadSample[0]+2&&reloadAttempts[0]<3)reloadAction[0]=null;
          if(name.startsWith("TFM")&&reloadAction[0]==null&&reloadAttempts[0]<3) {
            var selected=ProjectLifecycleActions.select(project,root.resolve("Host/BoundaryHost.csproj"));if(selected==null)return false;
            event("selected-real-native-project-for-explicit-reload",selected.observation());
            reloadAction[0]=ProjectLifecycleActions.perform(selected,"RiderReloadProjectAction");reloadAttempts[0]++;
            reloadSample[0]=sample==null?-1:sample.get("seq").getAsLong();
            event("native-project-reload-requested-not-completion",Map.of("id","RiderReloadProjectAction","attempt",reloadAttempts[0]));
          }
          return false;
        }
        require(sample.get("epoch").getAsLong()>heldEpoch||!sample.get("session").getAsString().equals(heldSession),"Host changed without invalidation identity");
        event("actual-reevaluated-host-state",sample);return true;
      });
      staleChecks(name);
      var recovered=new StableHost();
      setup(name+": restore original project and wait actual host recovery",() -> {recovered.reset();saveProject(text -> text);},() -> {
        if(!projectSaved())return false;
        var sample=sample();boolean ready=module(sample)!=null&&"net8.0".equals(module(sample).get("framework").getAsString())
            &&bound(sample,"BoundaryReferenceKey","Determined","ReferenceWorker")&&bound(sample,"BoundaryProjectKey","Determined","ProjectWorker")&&bound(sample,"BoundaryFrameworkKey","Determined","LegacyWorker");
        if(!ready){recovered.reset();return false;}return recovered.observe(sample);
      });
    }
    void staleChecks(String name) {
      step(name+": displayed DSL/native results actively expire, preserve row identity/order",() -> {},() -> {
        if(!expired(dsl)||!expired(nativePage))return false;
        require(rows(dsl).equals(dslRows),"DSL expired result reordered");
        return invalidNativeRows();
      });
      for(boolean keyboard:List.of(false,true)) {
        step(name+": old DSL row rejects "+(keyboard?"Enter":"doubleClick"),() -> {
          stationary();baselineNotifications();var window=ToolWindowManager.getInstance(project).getToolWindow("AnotherFSM");window.getContentManager().setSelectedContent(dsl);window.show(null);
          var list=list(dsl);require(list!=null&&list.getModel().getSize()>0,"Missing actual old DSL row");list.setSelectedIndex(0);list.ensureIndexIsVisible(0);gesture(list,keyboard);
        },() -> {
          require(stationaryNow(),"Expired DSL result jumped to old position");require(rows(dsl).equals(dslRows),"Stale DSL activation reordered rows");
          return notification(text -> text.matches("(?is).*(过期|stale|refresh|刷新).*"))!=null;
        });
        final long[] gestureTime={0};final Usage[] selectedUsage={null};
        step(name+": old native row rejects "+(keyboard?"EditSource command":"doubleClick"),() -> {
          stationary();var window=ToolWindowManager.getInstance(project).getToolWindow("Find");window.getContentManager().setSelectedContent(nativePage);window.show(null);
          String keyText=nativeRows.get(0).getAsJsonObject().get("currentSlice").getAsString();
          var view=UsageView.USAGE_VIEW_KEY.getData(DataManager.getInstance().getDataContext(nativePage.getComponent()));
          require(view instanceof UsageViewImpl,"Missing actual native UsageView");
          var matches=view.getUsages().stream().filter(usage -> usage.getPresentation().getPlainText().contains(keyText)).toArray(Usage[]::new);
          if(matches.length!=1){pending("Exact public native Usage cannot be uniquely identified; no activation pass.");return;}
          selectedUsage[0]=matches[0];view.selectUsages(matches);
          event("public-native-selection-requested",Map.of("text",matches[0].getPresentation().getPlainText(),"canNavigate",matches[0].canNavigate()));
        },() -> {
          if(steps.get(index).status.equals("pendingVerification"))return true;
          if(gestureTime[0]==0) {
            var view=UsageView.USAGE_VIEW_KEY.getData(DataManager.getInstance().getDataContext(nativePage.getComponent()));
            if(view==null||view.getSelectedUsages().size()!=1||!view.getSelectedUsages().contains(selectedUsage[0]))return false;
            JTree actualTree=null;
            for(var component:tree(nativePage.getComponent()))if(component instanceof JTree candidate&&candidate.isShowing()&&candidate.getSelectionCount()==1) {
              require(actualTree==null,"Multiple selected native trees; cannot correlate activation");actualTree=candidate;
            }
            if(actualTree==null)return false;
            event("selected-public-native-usage",Map.of("text",selectedUsage[0].getPresentation().getPlainText(),"canNavigate",selectedUsage[0].canNavigate(),
                "treePath",String.valueOf(actualTree.getSelectionPath()),"listeners",Arrays.stream(actualTree.getKeyListeners()).map(listener -> listener.getClass().getName()).toList()));
            if(keyboard)nativeEditSource(actualTree);else gesture(actualTree,false);
            gestureTime[0]=System.nanoTime();return false;
          }
          require(stationaryNow(),"Expired native result jumped to old position");return invalidNativeRows()&&ms(gestureTime[0])>=1250;
        });
      }
      step(name+": retained determined public target rejects navigateType after reload (SDK, not UI)",() -> {
        reply=null;var request=oldRequest.deepCopy();request.addProperty("action","navigateType");request.addProperty("requestId",UUID.randomUUID().toString());
        request.add("session",oldBinding.get("session"));request.add("contextVersion",oldBinding.get("contextVersion"));request.add("target",oldBinding.get("target"));query(request,result -> reply=result);
      },() -> {if(reply==null)return false;require("stale".equals(reply.get("status").getAsString()),"Old determined target accepted after reload: "+reply);event("public-stale-target-rejection-not-UI",reply);return true;});
    }
    void unloadCase() {
      String name="Native consumer-project unload";var warm=new StableHost();
      setup(name+": stable loaded baseline",() -> {open("Nodes/Nodes.cs","class ReferenceWorker","ReferenceWorker");open("Host/flow.fsm","def ReferenceEntry(BoundaryReferenceKey)","ReferenceEntry");warm.reset();},() -> {
        var state=sample();return module(state)!=null&&bound(state,"BoundaryReferenceKey","Determined","ReferenceWorker")&&warm.observe(state);
      });
      lists("ReferenceEntry","BoundaryReferenceKey","Nodes/Nodes.cs","ReferenceWorker");holdBinding("BoundaryReferenceKey");
      final ActionCallback[] unload={null};final UUID[] projectGuid={null};
      setup(name+": existing native project entity becomes Unloaded",() -> {
        before=sample();heldEpoch=before.get("epoch").getAsLong();
        require(!expired(dsl)&&!expired(nativePage)&&nativeRowsHaveValidity(true),"Unload baseline already expired");
      },() -> {
        var selected=ProjectLifecycleActions.select(project,root.resolve("Host/BoundaryHost.csproj"),projectGuid[0]);if(selected==null)return false;
        if(selected.entity().getDescriptor() instanceof RdUnloadProjectDescriptor descriptor) {
          require(descriptor.getState()==RdUnloadProjectState.Unloaded,"Host unload did not establish Unloaded: "+selected.observation());
          var nodes=ProjectLifecycleActions.inspect(project,root.resolve("Nodes/BoundaryNodes.csproj"));
          var contract=ProjectLifecycleActions.inspect(project,root.resolve("Contract/BoundaryContract.csproj"));
          var state=sample();if(state==null||state.getAsJsonArray("modules").size()!=0||nodes==null||contract==null)return false;
          require(nodes.entity().getDescriptor() instanceof RdProjectDescriptor&&contract.entity().getDescriptor() instanceof RdProjectDescriptor,"Unload affected another project");
          require(state.get("epoch").getAsLong()>heldEpoch,"Native unload did not invalidate context");
          event("actual-native-Unloaded-not-zero-result-inference",Map.of("host",selected.observation(),"nodes",nodes.observation(),"contract",contract.observation(),"backend",state));return true;
        }
        require(selected.entity().getDescriptor() instanceof RdProjectDescriptor,"Unexpected target project descriptor");
        if(unload[0]==null) {
          projectGuid[0]=((RdProjectDescriptor)selected.entity().getDescriptor()).getOriginalGuid();
          event("selected-real-native-project-for-unload",selected.observation());
          unload[0]=ProjectLifecycleActions.perform(selected,"RiderUnloadProjectAction");
          event("native-unload-requested-not-completion","RiderUnloadProjectAction");
        }
        return false;
      });
      staleChecks(name);
      step("unloaded consumer script cannot borrow the remaining solution's C# key",() -> {
        open("Host/flow.fsm","def ReferenceEntry(BoundaryReferenceKey)","BoundaryReferenceKey");baselineNotifications();action(IMPL);
      },() -> {
        var current=FileEditorManager.getInstance(project).getSelectedTextEditor();
        require(current!=null&&current.getDocument()==document("Host/flow.fsm")&&current.getCaretModel().getOffset()==offset(current.getDocument(),"def ReferenceEntry(BoundaryReferenceKey)","BoundaryReferenceKey"),"Unloaded script borrowed another project's C# target");
        return notification(text -> text.contains("C# host context unavailable"))!=null;
      });
      step("unloaded consumer script retains real local DSL declaration navigation",() -> {
        open("Host/flow.fsm","Pulse -> ReferenceEntry to ProjectEntry","ReferenceEntry");action("AnotherFSM.GoToDeclaration");
      },() -> {
        var current=FileEditorManager.getInstance(project).getSelectedTextEditor();return current!=null&&current.getDocument()==document("Host/flow.fsm")
            &&current.getCaretModel().getOffset()==offset(current.getDocument(),"def ReferenceEntry(BoundaryReferenceKey)","ReferenceEntry")
            &&"ReferenceEntry".equals(current.getSelectionModel().getSelectedText());
      });
      final ActionCallback[] load={null};var recovered=new StableHost();
      setup("native Load restores the same consumer and its actual binding",() -> recovered.reset(),() -> {
        var selected=ProjectLifecycleActions.select(project,root.resolve("Host/BoundaryHost.csproj"),projectGuid[0]);if(selected==null)return false;
        var state=sample();
        if(selected.entity().getDescriptor() instanceof RdProjectDescriptor&&module(state)!=null&&bound(state,"BoundaryReferenceKey","Determined","ReferenceWorker")) {
          if(!recovered.observe(state))return false;event("actual-native-Load-restored",Map.of("project",selected.observation(),"backend",state));return true;
        }
        if(load[0]==null) {event("selected-real-native-unloaded-project-for-load",selected.observation());load[0]=ProjectLifecycleActions.perform(selected,"RiderLoadProjectAction");}
        return false;
      });
      step("restored consumer C# navigation uses its loaded project context again",() -> {
        open("Host/flow.fsm","def ReferenceEntry(BoundaryReferenceKey)","BoundaryReferenceKey");action(IMPL);
      },() -> {
        var current=FileEditorManager.getInstance(project).getSelectedTextEditor();if(current==null||current.getDocument()!=document("Nodes/Nodes.cs"))return false;
        int line=current.getDocument().getLineNumber(current.getCaretModel().getOffset());
        return current.getDocument().getText().substring(current.getDocument().getLineStartOffset(line),current.getDocument().getLineEndOffset(line)).contains("class ReferenceWorker");
      });
    }
    boolean invalidNativeRows() {return nativeRowsHaveValidity(false);}
    boolean nativeRowsHaveValidity(boolean expectedValid) {
      var observation=json(output.resolve("native-observation.json"));if(observation==null)return false;
      require(observation.get("backendPid").getAsLong()==backendPid,"Wrong native observer backend");
      for(var value:observation.getAsJsonArray("descriptors")) {
        var descriptor=value.getAsJsonObject();if(descriptor.get("id").getAsInt()!=nativeId)continue;
        var current=new JsonArray();for(var item:descriptor.getAsJsonArray("items")) {
          var row=item.getAsJsonObject();if(row.has("runtimeType")&&!row.get("runtimeType").isJsonNull()&&"AnotherFSM.Rider.FsmOccurrence".equals(row.get("runtimeType").getAsString()))current.add(row);
        }
        require(current.size()==nativeRows.size(),"Expired native rows removed/replaced");
        for(int i=0;i<current.size();i++) {
          var old=nativeRows.get(i).getAsJsonObject();var row=current.get(i).getAsJsonObject();
          require(old.get("id").equals(row.get("id"))&&old.get("start").equals(row.get("start"))&&old.get("length").equals(row.get("length")),"Expired native exact row identity/order changed");
          if(row.get("valid").getAsBoolean()!=expectedValid)return false;
        }return true;
      }return false;
    }
    void gesture(JComponent component,boolean keyboard) {
      require(component.isShowing()&&component.isEnabled(),"Gesture requires actual visible enabled Swing component");
      event("actual-Swing-gesture",Map.of("class",component.getClass().getName(),"gesture",keyboard?"Enter":"doubleClick","physicalOsEvent",false));
      if(keyboard) {
        var stroke=KeyStroke.getKeyStroke(KeyEvent.VK_ENTER,0);
        for(int condition:new int[]{JComponent.WHEN_FOCUSED,JComponent.WHEN_ANCESTOR_OF_FOCUSED_COMPONENT,JComponent.WHEN_IN_FOCUSED_WINDOW}) {
          var key=component.getInputMap(condition).get(stroke);var action=key==null?null:component.getActionMap().get(key);
          if(action!=null&&action.isEnabled()){action.actionPerformed(new ActionEvent(component,ActionEvent.ACTION_PERFORMED,String.valueOf(key)));return;}
        }
        var listeners=component.getKeyListeners();require(listeners.length>0,"No actual public Enter action/listener");
        var pressed=new KeyEvent(component,KeyEvent.KEY_PRESSED,System.currentTimeMillis(),0,KeyEvent.VK_ENTER,KeyEvent.CHAR_UNDEFINED);
        for(var listener:listeners){listener.keyPressed(pressed);if(pressed.isConsumed())break;}
      } else {
        Rectangle bounds=component instanceof JList<?> list?list.getCellBounds(list.getSelectedIndex(),list.getSelectedIndex()):((JTree)component).getPathBounds(((JTree)component).getSelectionPath());
        require(bounds!=null&&component.getMouseListeners().length>0,"No actual selected cell/listeners");
        component.dispatchEvent(new MouseEvent(component,MouseEvent.MOUSE_CLICKED,System.currentTimeMillis(),0,bounds.x+5,bounds.y+bounds.height/2,2,false,MouseEvent.BUTTON1));
      }
    }
    void nativeEditSource(JTree component) {
      var action=ActionManager.getInstance().getAction("EditSource");require(action!=null,"Missing registered native source-navigation command");
      var context=DataManager.getInstance().getDataContext(component);
      require(CommonDataKeys.PROJECT.getData(context)==project&&UsageView.USAGE_VIEW_KEY.getData(context)!=null,"Native source command lost actual selected UsageView context");
      var event=AnActionEvent.createFromAnAction(action,null,ActionPlaces.USAGE_VIEW_TOOLBAR,context);action.update(event);
      event("registered-native-source-command",Map.of("id","EditSource","caption",String.valueOf(action.getTemplatePresentation().getText()),"enabled",event.getPresentation().isEnabled(),"physicalEnterClaimed",false));
      if(event.getPresentation().isEnabled())action.actionPerformed(event);
    }
    boolean decompilerApplicable(JsonObject policy) {
      for(var item:policy.getAsJsonArray("providers")) {var provider=item.getAsJsonObject();if(provider.get("runtimeType").getAsString().endsWith(".DecompiledSourcesExternalSourcesProvider"))return provider.get("applicable").getAsBoolean();}return false;
    }
    boolean policyReady(JsonObject policy) {return policy.get("active").getAsBoolean()&&!policy.get("forceCookie").getAsBoolean();}
    void policyCase(String name,Predicate<JsonObject> prerequisite,String notEstablished) {
      step(name+": establish actual effective policy (read-only)",() -> {
        policyEstablished=false;var sample=sample();
        if(sample==null||!sample.has("policy")||sample.get("policy").isJsonNull()||!bound(sample,"HostDllKey","Determined","BinaryWorker")){pending("No actual compiled determined target/policy observation");return;}
        policyBefore=sample.deepCopy();var policy=sample.getAsJsonObject("policy");
        if(!policyReady(policy)||!prerequisite.test(policy)){pending(notEstablished+" No settings were changed.");return;}
        policyEstablished=true;event("actual-policy-before",policyBefore);
      },() -> true);
      step(name+": formal navigation retains determined binding + metadata/unverified",() -> {
        if(!policyEstablished){pending("Prerequisite not established; no action run for this policy case");return;}
        policyReason=null;policyResponseSequence=-1;
        open("Host/flow.fsm","def BinaryEntry(HostDllKey)","HostDllKey");baselineNotifications();
        priorWindows.clear();for(var window:Window.getWindows())if(window.isShowing())priorWindows.add(window);action(IMPL);
      },() -> {
        if(!policyEstablished)return true;
        var sample=sample();if(sample==null)return false;
        if(policyReason==null) {
          policyReason=notification(text -> text.contains("Determined C# binding retained")&&text.contains("BinaryWorker")&&text.toLowerCase(Locale.ROOT).contains("source match unverified"));
          if(policyReason==null)return false;
          policyResponseSequence=sample.get("seq").getAsLong();return false;
        }
        // Require a read-only sample taken AFTER observing the completed formal action's reason.
        if(sample.get("seq").getAsLong()<=policyResponseSequence)return false;
        var after=sample.getAsJsonObject("policy");require(after!=null&&after.equals(policyBefore.getAsJsonObject("policy")),"Policy/identity changed across formal action; no stable negative-case pass");
        require(bound(sample,"HostDllKey","Determined","BinaryWorker"),"External binding was discarded after policy refusal");
        String reason=policyReason;
        require(!reason.contains("Decompiled code")&&!reason.contains("decompiledNavigationDispatched"),"Negative policy dispatched decompiler");
        require(reason.contains("assembly=")&&reason.contains("token=0x")&&reason.contains("module=")&&reason.contains("consuming TFM="),"Missing retained full external metadata/consuming identity");
        require(reason.contains("Metadata")||reason.contains("metadata"),"Missing retained metadata presentation");
        if(name.startsWith("Unaccepted")&&!reason.contains("legal notice has not already been accepted")) {
          pending("Formal action retained metadata, but did not reach the consent guard; this is not a consent-negative pass.");return true;
        }
        if(name.startsWith("Host effectively")&&!reason.contains("decompiler is not applicable")) {
          pending("Formal action retained metadata, but effective-disable provider guard not reached.");return true;
        }
        for(var window:Window.getWindows())require(!window.isShowing()||priorWindows.contains(window)||!(window instanceof Dialog dialog)||!dialog.isModal(),"Unexpected new modal dialog during negative policy action; no consent interaction authorized");
        require(!reason.toLowerCase(Locale.ROOT).matches("(?s).*(offline|unauthorized|private feed|401).*$"),"Product guessed an unobserved external cause");
        event("actual-policy-after",sample);event("external-negative-scope","Formal action and visible retained metadata/unverified verified; dispatch alone is not editor/source navigation success. No consent UI accepted and no settings written.");
        if(name.startsWith("Observed PDB"))pending("Observed PDB Download=false plus actual metadata fallback only; effective host source-download blocking was NOT exercised, so download-disabled AC remains pending.");
        return true;
      });
    }
    void specify() {
      setup("verify independent live sandbox and complete host readiness",() -> {
        require(root.endsWith(Path.of("rider/tests/fixtures/boundary")),"Boundary assets only; never mutate Complex fixtures");
      },() -> {
        if(!sandbox())return false;var sample=sample();if(module(sample)==null)return false;
        return "net8.0".equals(module(sample).get("framework").getAsString())&&bound(sample,"BoundaryReferenceKey","Determined","ReferenceWorker")
            &&bound(sample,"BoundaryProjectKey","Determined","ProjectWorker")&&bound(sample,"BoundaryFrameworkKey","Determined","LegacyWorker");
      });
      reload("Project Compile inventory reload","BoundaryProjectKey","ProjectEntry","Host/Nodes.cs","ProjectWorker",
          text -> text.replace("  </ItemGroup>","    <Compile Remove=\"Nodes.cs\" />\n  </ItemGroup>"),
          sample -> bound(sample,"BoundaryProjectKey","Unresolved",null)&&bound(sample,"BoundaryFrameworkKey","Unresolved",null));
      reload("ProjectReference removal reload","BoundaryReferenceKey","ReferenceEntry","Nodes/Nodes.cs","ReferenceWorker",
          text -> text.replace("    <ProjectReference Include=\"../Nodes/BoundaryNodes.csproj\" />\n",""),
          sample -> bound(sample,"BoundaryReferenceKey","Unresolved",null)&&!module(sample).getAsJsonArray("references").toString().contains("BoundaryNodes.csproj"));
      reload("TFM replacement reload","BoundaryFrameworkKey","FrameworkEntry","Host/Nodes.cs","LegacyWorker",
          text -> text.replace("<TargetFramework>net8.0</TargetFramework>","<TargetFramework>net10.0</TargetFramework>"),
          sample -> "net10.0".equals(module(sample).get("framework").getAsString())&&bound(sample,"BoundaryFrameworkKey","Determined","ModernWorker"));
      unloadCase();
      policyCase("Unaccepted decompiler legal notice",policy -> !policy.get("consent").getAsBoolean()&&policy.get("effectiveBodies").getAsBoolean()&&decompilerApplicable(policy),
          "Unaccepted-consent guard with otherwise applicable decompiler not established.");
      policyCase("Host effectively disables decompilation",policy -> !policy.get("effectiveBodies").getAsBoolean()&&!decompilerApplicable(policy),
          "Effective decompile=false AND concrete decompiler applicability=false not observed; raw settings do not suffice.");
      policyCase("Observed PDB download setting false with decompiler independently blocked", policy -> !policy.get("downloadPdb").getAsBoolean()
          &&(!policy.get("consent").getAsBoolean()||(!policy.get("effectiveBodies").getAsBoolean()&&!decompilerApplicable(policy))),
          "Host symbol-download policy false and independently blocked decompiler not observed; this does not cover every host source-download mechanism.");
      step("Project-unassociated file refuses borrowed C# context (not an unloaded/untrusted project claim)",() -> {
        open("loose.fsm","def LooseEntry(BoundaryReferenceKey)","BoundaryReferenceKey");baselineNotifications();action(IMPL);
      },() -> {
        var current=FileEditorManager.getInstance(project).getSelectedTextEditor();require(current!=null&&current.getDocument()==document("loose.fsm"),"Loose script borrowed another host's C# target");
        return notification(text -> text.contains("C# host context unavailable"))!=null;
      });
      step("text from the natively trust-declined solution cannot borrow another project's C# binding",() -> {
        open("../trust-declined/flow.fsm","def LocalOnly(BoundaryReferenceKey)","BoundaryReferenceKey");baselineNotifications();action(IMPL);
      },() -> {
        var current=FileEditorManager.getInstance(project).getSelectedTextEditor();require(current!=null&&current.getDocument()==document("../trust-declined/flow.fsm"),"Trust-declined text borrowed a loaded project's C# type");
        return notification(text -> text.contains("C# host context unavailable"))!=null;
      });
      step("text from the trust-declined solution retains local DSL navigation without loading it",() -> {
        open("../trust-declined/flow.fsm","Pulse -> LocalOnly to LocalOnly","LocalOnly");action("AnotherFSM.GoToDeclaration");
      },() -> {
        var current=FileEditorManager.getInstance(project).getSelectedTextEditor();return current!=null&&current.getDocument()==document("../trust-declined/flow.fsm")
            &&current.getCaretModel().getOffset()==offset(current.getDocument(),"def LocalOnly(BoundaryReferenceKey)","LocalOnly")
            &&"LocalOnly".equals(current.getSelectionModel().getSelectedText());
      });
      once("record bounded unverified AC",() -> {
        String marker=System.getenv("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");if(marker==null||marker.isBlank())pending.add("Execution sentinel not configured; absence cannot prove instrumented fixture safety.");
        event("remaining-AC-pending",pending);
      });
    }
    boolean restore() {
      if(projectDocument==null)return true;
      try {
        var path=root.resolve("Host/BoundaryHost.csproj");
        String diskBefore=Files.readString(path).replace("\r\n","\n");
        require(projectDocument.getText().equals(ownedProject)||projectDocument.getText().equals(originalProject),"Concurrent project buffer edit; refusing cleanup overwrite");
        if(!diskBefore.equals(ownedProject)&&!diskBefore.equals(originalProject)) {
          if(ms(lastProjectSave)<10_000)return false;
          throw new IllegalStateException("Concurrent/unfinished project disk edit; refusing cleanup overwrite");
        }
        if(!projectDocument.getText().equals(originalProject))WriteCommandAction.runWriteCommandAction(project,() -> projectDocument.setText(originalProject));
        if(!restoreRequested) {
          restoreRequested=true;lastProjectSave=System.nanoTime();FileDocumentManager.getInstance().saveDocument(projectDocument);return false;
        }
        if(!diskBefore.equals(originalProject))return false;
        // Only our independently owned, fully read-back original text. Restore encoding/BOM/EOL exactly.
        if(!Arrays.equals(Files.readAllBytes(path),originalBytes)) {Files.write(path,originalBytes);file("Host/BoundaryHost.csproj").refresh(false,false);return false;}
        event("restored-original-project-bytes",path.toString());return true;
      }catch(java.io.IOException error){throw new IllegalStateException(error);}
    }
    void report(String status,String error) {
      try {
        Files.createDirectories(output);var coverage=new ArrayList<Object>();
        for(var step:steps){var item=new JsonObject();item.addProperty("name",step.name);item.addProperty("status",step.status);item.addProperty("detail",step.detail);coverage.add(item);}
        var report=new JsonObject();report.addProperty("status",status);report.addProperty("error",error);report.addProperty("runId",runId);report.addProperty("step",index);
        report.addProperty("frontendPid",ProcessHandle.current().pid());report.addProperty("backendPid",backendPid);report.addProperty("elapsedMs",ms(started));
        report.addProperty("seam","actual saved project editor + registered actions + visible Swing models/listeners; public SDK observer corroborates, never substitutes");
        report.add("coverage",gson.toJsonTree(coverage));report.add("observations",gson.toJsonTree(events));report.add("pendingVerification",gson.toJsonTree(pending));
        report.add("notClaimed",gson.toJsonTree(List.of("#35 documentation/completion","#36 performance","verified external source","whole-machine offline/private authorization","physical OS keyboard/mouse input")));
        Files.writeString(output.resolve("complex-boundary-acceptance.json"),gson.toJson(report));
      }catch(Exception failure){failure.printStackTrace();}
    }
    void finish(boolean successful,String error) {
      if(done||finishing)return;finishing=true;cleanupSuccess=successful;cleanupError=error;cleanupStarted=System.nanoTime();
      requestGeneration++;if(query!=null)Disposer.dispose(query);
      cleanupIncomplete=!successful&&index<steps.size()&&steps.get(index).status.equals("pendingVerification");
      if(!successful&&index<steps.size()&&!cleanupIncomplete){steps.get(index).status="failed";steps.get(index).detail=error;}
      report("cleaningUp",error);finishCleanup();
    }
    void finishCleanup() {
      try {
        require(ms(cleanupStarted)<15_000,"Timed out waiting for exact project cleanup readback; no unknown bytes overwritten");
        if(!restore())return;
        sentinel();for(var page:ownedPages)if(!Disposer.isDisposed(page))page.setPinned(false);
        boolean awaitingNativeRestore=false;
        if(isolation) {
          var selected=ProjectLifecycleActions.select(project,root.resolve("Host/BoundaryHost.csproj"));
          if(selected!=null&&selected.entity().getDescriptor() instanceof RdUnloadProjectDescriptor unloaded&&unloaded.getState()==RdUnloadProjectState.Unloaded) {
            ProjectLifecycleActions.perform(selected,"RiderLoadProjectAction");awaitingNativeRestore=true;
            event("cleanup-native-Load-requested-not-confirmed",selected.observation());
          }
        }
        var command=json(output.resolve("complex-boundary-observe.json"));
        if(!awaitingNativeRestore&&command!=null&&runId.equals(command.get("runId").getAsString()))Files.deleteIfExists(output.resolve("complex-boundary-observe.json"));
      }
      catch(Throwable cleanup){cleanupSuccess=false;cleanupIncomplete=false;cleanupError=String.valueOf(cleanupError)+"; cleanup="+cleanup;event("cleanup-failed",cleanup.toString());}
      done=true;if(timer!=null)timer.cancel(false);
      report(!cleanupSuccess?(cleanupIncomplete?"pendingVerification":"failed"):pending.isEmpty()&&steps.stream().noneMatch(step -> step.status.equals("pendingVerification"))?"passed":"partial",cleanupError);
      if(cleanupSuccess&&continuation!=null)continuation.run();
    }
  }
}
