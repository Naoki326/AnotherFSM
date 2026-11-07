package anotherfsm.acceptance;

import anotherfsm.BackendConnection;
import com.google.gson.*;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.actionSystem.*;
import com.intellij.ide.DataManager;
import com.intellij.ui.content.Content;
import com.intellij.usages.UsageView;
import com.intellij.usages.impl.UsageViewImpl;
import com.jetbrains.rdclient.usages.FrontendUsageTarget;
import com.jetbrains.rd.ide.model.RdFindUsagesSession;
import com.jetbrains.rider.protocol.ApplicationProtocolKt;
import com.jetbrains.rider.projectView.SolutionHostExtensionsKt;
import com.anotherfsm.rider.protocol.AnotherFsmModel_GeneratedKt;
import kotlin.Unit;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.command.WriteCommandAction;
import com.intellij.openapi.editor.*;
import com.intellij.openapi.fileEditor.*;
import com.intellij.openapi.project.*;
import com.intellij.openapi.startup.StartupActivity;
import com.intellij.openapi.util.Disposer;
import com.intellij.openapi.vfs.*;
import com.intellij.openapi.wm.ToolWindowManager;
import com.intellij.util.concurrency.AppExecutorUtil;
import java.awt.*;
import java.nio.file.*;
import java.util.*;
import java.util.List;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.imageio.ImageIO;

/** Separate, sandbox-only test plugin. Invokes installed product actions, never fabricates targets. */
public final class HostAcceptance implements StartupActivity.DumbAware {
  public void runActivity(Project project) {
    if(!Boolean.getBoolean("anotherfsm.acceptance")||Boolean.getBoolean("anotherfsm.complexAcceptance")||Boolean.getBoolean("anotherfsm.boundaryAcceptance")) return;
    new Journey(project).start();
  }
  private static final class Journey {
    final Project project;
    final Path output=Path.of(System.getProperty("anotherfsm.acceptance.dir"));
    final Path root=Path.of(System.getProperty("anotherfsm.fixture.root"));
    final List<JsonObject> observations=new ArrayList<>();
    final Map<Document,String> originals=new HashMap<>();
    long begun;
    final AtomicBoolean tickQueued=new AtomicBoolean();
    Set<Integer> contextBaseline=Set.of();
    String oldLookupListId,freshLookupListId;JsonObject oldExpiry;
    Content oldLookupContent;
    ScheduledFuture<?> timer; int stage; long stageStart; Disposable query; boolean inFlight; boolean done; Editor editor;
    Journey(Project project) { this.project=project; }
    void start() {
      timer=AppExecutorUtil.getAppScheduledExecutorService().scheduleWithFixedDelay(() -> {
        if(!tickQueued.compareAndSet(false,true)) return;
        ApplicationManager.getApplication().invokeLater(() -> {
          try { tick(); } catch(Throwable failure) { finish(false,failure.toString()); }
          finally { tickQueued.set(false); }
        });
      },0,250,TimeUnit.MILLISECONDS);
    }
    boolean elapsed(long milliseconds) { return (System.nanoTime()-stageStart)/1_000_000>milliseconds; }
    void next() { stage++; stageStart=System.nanoTime(); report("running",null); }
    void record(String name,Object value) { var item=new JsonObject(); item.addProperty("name",name); item.add("value",new Gson().toJsonTree(value)); observations.add(item); }
    VirtualFile file(String relative) { return LocalFileSystem.getInstance().refreshAndFindFileByNioFile(root.resolve(relative)); }
    Document document(String relative) {
      var file=file(relative); if(file==null) throw new IllegalStateException("Missing fixture "+relative);
      var document=FileDocumentManager.getInstance().getDocument(file); if(document==null) throw new IllegalStateException("No document "+relative);
      originals.putIfAbsent(document,document.getText()); return document;
    }
    void open(String relative,String needle) {
      var document=document(relative); int offset=document.getText().indexOf(needle);
      if(offset<0) throw new IllegalStateException("Fixture marker missing: "+needle);
      editor=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,file(relative),offset),true);
      if(editor==null) throw new IllegalStateException("Editor unavailable");
      editor.getSelectionModel().removeSelection(); editor.getCaretModel().moveToOffset(offset);
    }
    void action(String id) {
      var action=ActionManager.getInstance().getAction(id); if(action==null) throw new IllegalStateException("Missing registered action "+id);
      record("action",id);
      var context=DataManager.getInstance().getDataContext(editor.getContentComponent());
      if(CommonDataKeys.EDITOR.getData(context)!=editor || CommonDataKeys.PROJECT.getData(context)!=project)
        throw new IllegalStateException("Native action lost its real editor/project context");
      action.actionPerformed(AnActionEvent.createFromAnAction(action,null,ActionPlaces.EDITOR_POPUP,context));
    }
    void rerunNativeSearch() {
      var window=ToolWindowManager.getInstance(project).getToolWindow("Find");
      if(window==null || window.getContentManager().getSelectedContent()==null) throw new IllegalStateException("No actual native Find tab to refresh");
      var component=window.getContentManager().getSelectedContent().getComponent();
      var context=DataManager.getInstance().getDataContext(component);
      var action=ActionManager.getInstance().getAction("UsageView.Rerun");
      record("action","UsageView.Rerun");
      action.actionPerformed(AnActionEvent.createFromAnAction(action,null,ActionPlaces.USAGE_VIEW_TOOLBAR,context));
    }
    String nativeTabTitle() {
      var window=ToolWindowManager.getInstance(project).getToolWindow("Find");
      return window==null||window.getContentManager().getSelectedContent()==null?"":window.getContentManager().getSelectedContent().getDisplayName();
    }
    Content nativeContent() {return ToolWindowManager.getInstance(project).getToolWindow("Find").getContentManager().getSelectedContent();}
    RdFindUsagesSession nativeSession(Content content) {
      var view=UsageView.USAGE_VIEW_KEY.getData(DataManager.getInstance().getDataContext(content.getComponent()));
      if(!(view instanceof UsageViewImpl nativeView))throw new IllegalStateException("No actual native usage view");
      var sessions=Collections.newSetFromMap(new IdentityHashMap<RdFindUsagesSession,Boolean>());
      for(var target:nativeView.getTargets())if(target instanceof FrontendUsageTarget nativeTarget)sessions.add(nativeTarget.getSession());
      if(sessions.size()!=1)throw new IllegalStateException("Native page has no unique session");return sessions.iterator().next();
    }
    void status(String listId,java.util.function.Consumer<JsonObject> success) {
      if(inFlight)return;
      var request=new JsonObject();request.addProperty("action","referenceStatus");request.addProperty("listId",listId);
      request.addProperty("path","untitled");request.addProperty("text","");request.addProperty("documentVersion",0);
      request.addProperty("offset",0);request.addProperty("requestId",UUID.randomUUID().toString());
      inFlight=true;
      query=BackendConnection.get(project).query(request.toString(),raw -> {inFlight=false;if(!done)success.accept(JsonParser.parseString(raw).getAsJsonObject());},failure -> {inFlight=false;finish(false,failure.toString());});
    }
    void replayOldExpiry() {
      var packet=new JsonObject();packet.add("session",oldExpiry.get("session"));packet.add("contextVersion",oldExpiry.get("contextVersion"));
      var expired=new JsonArray();expired.add(oldExpiry.get("reference"));packet.add("expiredReferences",expired);
      var host=ApplicationProtocolKt.getProtocolHostIfExists(project);
      host.getProtocol().getScheduler().queue(() -> {
        AnotherFsmModel_GeneratedKt.getAnotherFsmModel(SolutionHostExtensionsKt.getSolution(project)).getInvalidated().fire(packet.toString());return Unit.INSTANCE;
      });
      record("replayed-actual-old-query-expiry",packet);
    }
    void probe(String relative,String needle,String expectedType,Runnable success) {
      if(inFlight) { if(elapsed(5000)) { if(query!=null) Disposer.dispose(query); record("probe-cancel-timeout",relative); inFlight=false; stageStart=System.nanoTime(); } return; }
      var document=document(relative); var request=new JsonObject();
      request.addProperty("action","resolve"); request.addProperty("path",file(relative).getPath()); request.addProperty("text",document.getText());
      request.addProperty("documentVersion",document.getModificationStamp()); request.addProperty("offset",document.getText().indexOf(needle));
      request.addProperty("requestId",UUID.randomUUID().toString()); request.addProperty("contextVersion",-1);
      inFlight=true; stageStart=System.nanoTime();
      query=BackendConnection.get(project).query(request.toString(),raw -> {
        if(done) return;
        inFlight=false;
        var reply=JsonParser.parseString(raw).getAsJsonObject();
        var expectedStatus=expectedType==null?"unresolved":"type";
        if(!reply.has("status")||!reply.get("status").getAsString().equals(expectedStatus)) { record("probe-pending",reply); return; }
        if(expectedType!=null) {
          var type=reply.getAsJsonObject("target").get("clrName").getAsString();
          if(!type.equals(expectedType)) { finish(false,"Wrong actual RD target: "+reply); return; }
        }
        record("actual-rd-result",reply); success.run();
      },failure -> { inFlight=false; record("probe-not-ready",failure.toString()); });
    }
    String selectedPath() {
      var selected=FileEditorManager.getInstance(project).getSelectedTextEditor(); if(selected==null) return "";
      var file=FileDocumentManager.getInstance().getFile(selected.getDocument()); return file==null?"":file.getPath();
    }
    List<JsonObject> nativeRows(boolean expired) throws Exception {
      var path=output.resolve("native-observation.json"); if(!Files.isRegularFile(path)) return List.of();
      var root=JsonParser.parseString(Files.readString(path)).getAsJsonObject(); var rows=new ArrayList<JsonObject>();
      for(var value:root.getAsJsonArray("descriptors")) {
        var descriptor=value.getAsJsonObject(); if(descriptor.get("updating").getAsBoolean()) continue;
        for(var item:descriptor.getAsJsonArray("items")) {
          var row=item.getAsJsonObject();
          if(row.has("file")&&row.get("file").getAsString().endsWith("Orders/orders.fsm")
              && row.get("text").getAsString().contains("AnotherFSM") && row.get("valid").getAsBoolean()!=expired) rows.add(row);
        }
      }
      return rows;
    }
    void tick() throws Exception {
      if(done) return;
      // First-launch onboarding is modal. Start the test clock when its first EDT operation can run.
      if(begun==0) {
        begun=System.nanoTime();
        var manager=FileEditorManager.getInstance(project);
        for(var relative:List.of("Orders/orders.fsm","Inventory/stock.FSMScript","../tests/fixtures/loose/standalone.fsm")) {
          var candidate=file(relative); if(candidate!=null) manager.closeFile(candidate);
        }
      }
      if(project.isDisposed()) { finish(false,"Project disposed during acceptance"); return; }
      if((System.nanoTime()-begun)/1_000_000>180000) { finish(false,"Actual Rider acceptance timeout at stage "+stage); return; }
      switch(stage) {
        case 0 -> probe("Orders/orders.fsm","BusinessQuery)","Orders.Container+LookupWorker",() -> { record("orders-script-unopened",!FileEditorManager.getInstance(project).isFileOpen(file("Orders/orders.fsm"))); next(); });
        case 1 -> { open("Orders/orders.fsm","Query to"); next(); }
        case 2 -> { action("AnotherFSM.GoToDeclaration"); next(); }
        case 3 -> { if("Query".equals(editor.getSelectionModel().getSelectedText()) && editor.getCaretModel().getOffset()==52) { record("dsl-declaration-selection",editor.getCaretModel().getOffset()); capture("dsl-declaration.png"); next(); } }
        case 4 -> { editor.getCaretModel().moveToOffset(editor.getDocument().getText().indexOf("BusinessQuery)")); action("AnotherFSM.GoToDeclaration"); next(); }
        case 5 -> { if(selectedPath().endsWith("Orders/Nodes.cs")) { record("orders-csharp-navigation",selectedPath()); capture("orders-csharp.png"); next(); } }
        case 6 -> {
          FileEditorManager.getInstance(project).closeFile(file("Orders/orders.fsm"));
          record("native-search-script-closed",!FileEditorManager.getInstance(project).isFileOpen(file("Orders/orders.fsm")));
          editor=FileEditorManager.getInstance(project).getSelectedTextEditor(); editor.getSelectionModel().removeSelection();
          editor.getCaretModel().moveToOffset(editor.getDocument().getText().indexOf("LookupWorker :")); action("FindUsages"); next();
        }
        case 7 -> {
          var rows=nativeRows(false);
          var starts=rows.stream().map(row -> row.get("start").getAsInt()).distinct().sorted().toList();
          if(starts.equals(List.of(58,116))) { record("native-csharp-declaration-factory-ranges",rows); capture("native-references.png"); next(); }
        }
        case 8 -> {
          open("Orders/orders.fsm","BusinessQuery)");
          action("AnotherFSM.GoToDeclaration");
          var doc=editor.getDocument(); int offset=doc.getText().indexOf("BusinessQuery)");
          WriteCommandAction.runWriteCommandAction(project,() -> doc.replaceString(offset,offset+13,"MissingKey"));
          record("unsaved-buffer",FileDocumentManager.getInstance().isDocumentUnsaved(doc));
          record("disk-still-original",Files.readString(root.resolve("Orders/orders.fsm")).equals(originals.get(doc)));
          next();
        }
        case 9 -> {
          if(elapsed(2000) && !nativeRows(true).isEmpty() && nativeTabTitle().startsWith("结果已过期")) {
            if(!selectedPath().endsWith("Orders/orders.fsm")) { finish(false,"Late reply navigated after unsaved edit"); return; }
            record("late-reply-did-not-navigate",true); record("visible-expired-native-tab",nativeTabTitle()); record("expired-native-rows",nativeRows(true)); capture("expired-native-references.png");
            next();
          }
        }
        case 10 -> { rerunNativeSearch(); next(); }
        case 11 -> {
          var rows=nativeRows(false); var expected=editor.getDocument().getText().indexOf("BusinessQuery()");
          if(rows.size()==1 && rows.get(0).get("start").getAsInt()==expected && !nativeTabTitle().startsWith("结果已过期") && !nativeTabTitle().startsWith("刷新中")) {
            record("native-refresh-uses-current-unsaved-key",rows); capture("refreshed-native-references.png");
            var doc=editor.getDocument(); WriteCommandAction.runWriteCommandAction(project,() -> doc.setText(originals.get(doc))); next();
          }
        }
        case 12 -> probe("Inventory/stock.FSMScript","PayloadTransform)","Inventory.ChangeWorker",this::next);
        case 13 -> { open("Inventory/stock.FSMScript","PayloadTransform)"); next(); }
        case 14 -> { action("AnotherFSM.GoToDeclaration"); next(); }
        case 15 -> { if(selectedPath().endsWith("Inventory/Nodes.cs")) { record("independent-inventory-csharp-navigation",selectedPath()); capture("inventory-csharp.png"); next(); } }
        case 16 -> { open("Orders/orders.fsm","BusinessQuery)"); var doc=editor.getDocument(); WriteCommandAction.runWriteCommandAction(project,() -> doc.setText("def Query(BusinessQuery) {1->")); next(); }
        case 17 -> probe("Orders/orders.fsm","BusinessQuery)","Orders.Container+LookupWorker",this::next);
        case 18 -> { var doc=editor.getDocument(); WriteCommandAction.runWriteCommandAction(project,() -> doc.setText("def Query(BusinessQue")); next(); }
        case 19 -> probe("Orders/orders.fsm","BusinessQue",null,() -> { var doc=editor.getDocument(); WriteCommandAction.runWriteCommandAction(project,() -> doc.setText(originals.get(doc))); next(); });
        case 20 -> { open("Orders/Nodes.cs","LookupWorker);"); next(); }
        case 21 -> { action("FindUsages"); next(); }
        case 22 -> {
          var rows=nativeRows(false); var starts=rows.stream().map(row -> row.get("start").getAsInt()).distinct().sorted().toList();
          if(starts.equals(List.of(58,116))) { record("native-csharp-type-use-factory-ranges",rows); capture("native-type-use.png"); next(); }
        }
        case 23 -> probe("Orders/orders.fsm","Conflict)",null,this::next);
        case 24 -> probe("Orders/orders.fsm","Generic)",null,this::next);
        case 25 -> probe("Orders/orders.fsm","NotNode)",null,this::next);
        case 26 -> probe("Orders/orders.fsm","Impostor)",null,this::next);
        case 27 -> probe("Orders/orders.fsm","ForbiddenFallback)",null,this::next);
        case 28 -> probe("Orders/orders.fsm","OuterGeneric)",null,this::next);
        case 29 -> { open("../tests/fixtures/loose/standalone.fsm","Local to"); next(); }
        case 30 -> { action("AnotherFSM.GoToDeclaration"); next(); }
        case 31 -> {
          if("Local".equals(editor.getSelectionModel().getSelectedText()) && editor.getCaretModel().getOffset()==4) {
            record("no-host-local-declaration",true); next();
          }
        }
        case 32 -> probe("../tests/fixtures/loose/standalone.fsm","LoneKey)",null,this::next);
        case 33 -> { open("Orders/Nodes.cs","LookupWorker);"); action("FindUsages"); next(); }
        case 34 -> {
          var rows=nativeRows(false);
          if(rows.stream().map(row -> row.get("start").getAsInt()).distinct().sorted().toList().equals(List.of(58,116))) {
            contextBaseline=new HashSet<>(); for(var row:rows) contextBaseline.add(row.get("id").getAsInt());
            record("csharp-context-valid-baseline",rows); next();
          }
        }
        case 35 -> {
          var doc=document("Orders/Nodes.cs"); var text=doc.getText(); var marker="Key = \"BusinessQuery\""; int offset=text.indexOf(marker);
          if(offset<0) throw new IllegalStateException("Missing attribute fixture marker");
          WriteCommandAction.runWriteCommandAction(project,() -> doc.replaceString(offset,offset+marker.length(),"Key = \"ChangedKey\""));
          record("unsaved-csharp-key",FileDocumentManager.getInstance().isDocumentUnsaved(doc)); next();
        }
        case 36 -> probe("Orders/orders.fsm","BusinessQuery)",null,this::next);
        case 37 -> {
          var expired=nativeRows(true); var ids=new HashSet<Integer>(); for(var row:expired) ids.add(row.get("id").getAsInt());
          if(ids.containsAll(contextBaseline)) {
            record("csharp-context-expires-native-rows",expired);
            var doc=document("Orders/Nodes.cs"); WriteCommandAction.runWriteCommandAction(project,() -> doc.setText(originals.get(doc))); next();
          }
        }
        case 38 -> probe("Orders/orders.fsm","BusinessQuery)","Orders.Container+LookupWorker",this::next);
        case 39 -> { open("Orders/UnusedWorker.cs","UnusedWorker :"); action("FindUsages"); next(); }
        case 40 -> { if(nativeTabTitle().equals("Usages of 'UnusedWorker'")) {record("zero-dsl-query-before-change",nativeTabTitle());next();} }
        case 41 -> {
          var doc=document("Orders/UnusedWorker.cs");var marker="[FSMNode(\"NeverUsed\")]";int offset=doc.getText().indexOf(marker);
          if(offset<0)throw new IllegalStateException("Missing zero-reference attribute");
          WriteCommandAction.runWriteCommandAction(project,() -> doc.deleteString(offset,offset+marker.length()));next();
        }
        case 42 -> { if(nativeTabTitle().startsWith("结果已过期")&&nativeTabTitle().contains("UnusedWorker")) {
          record("zero-dsl-query-expires-after-attribute-removal",true);
          var doc=document("Orders/UnusedWorker.cs");WriteCommandAction.runWriteCommandAction(project,() -> doc.setText(originals.get(doc)));next();
        } }
        case 43 -> {open("Orders/Nodes.cs","LookupWorker);");action("FindUsages");next();}
        case 44 -> {
          if(nativeTabTitle().equals("Usages of 'LookupWorker'")) {
            var model=nativeSession(nativeContent());if(!Boolean.TRUE.equals(model.isSearchComplete().getValueOrNull()))break;
            oldLookupContent=nativeContent();oldLookupContent.setPinned(true);oldLookupListId=model.getUsages().getListId();
            record("old-same-title-page",oldLookupListId);next();
          }
        }
        case 45 -> {
          open("Orders/orders.fsm","BusinessQuery)");var doc=editor.getDocument();int offset=doc.getText().indexOf("BusinessQuery)");
          WriteCommandAction.runWriteCommandAction(project,() -> doc.replaceString(offset,offset+13,"MissingKey"));next();
        }
        case 46 -> {if(oldLookupContent.getDisplayName().startsWith("结果已过期"))status(oldLookupListId,reply -> {
          if(!reply.has("reference")||reply.get("reference").isJsonNull())throw new IllegalStateException("Old native query identity missing");
          oldExpiry=reply.deepCopy();next();
        });}
        case 47 -> {open("Orders/Nodes.cs","LookupWorker);");action("FindUsages");next();}
        case 48 -> {
          var model=nativeSession(nativeContent());freshLookupListId=model.getUsages().getListId();
          if(!freshLookupListId.equals(oldLookupListId)&&Boolean.TRUE.equals(model.isSearchComplete().getValueOrNull())) {
            if(!model.getTitle().equals(nativeSession(oldLookupContent).getTitle()))throw new IllegalStateException("Test requires two same-title native pages");
            record("fresh-same-title-page",freshLookupListId);replayOldExpiry();next();
          }
        }
        case 49 -> {if(elapsed(750)) {
          if(nativeContent().getDisplayName().startsWith("结果已过期")||nativeContent().getDisplayName().startsWith("刷新中"))throw new IllegalStateException("Old other-page packet overwrote fresh same-title native page");
          record("same-title-fresh-page-not-invalidated",true);
          ToolWindowManager.getInstance(project).getToolWindow("Find").getContentManager().setSelectedContent(oldLookupContent);rerunNativeSearch();next();
        }}
        case 50 -> {if(!oldLookupContent.getDisplayName().startsWith("结果已过期")&&!oldLookupContent.getDisplayName().startsWith("刷新中")) {
          replayOldExpiry();next();
        }}
        case 51 -> {if(elapsed(750)) {
          if(oldLookupContent.getDisplayName().startsWith("结果已过期"))throw new IllegalStateException("Late previous-generation expiry overwrote refreshed native page");
          record("late-old-generation-expiry-ignored",true);capture("same-title-native-pages.png");finish(true,null);
        }}
      }
    }
    void capture(String name) throws Exception {
      for(var window:Window.getWindows()) if(window.isShowing() && window instanceof Frame && window.getWidth()>500) {
        Files.createDirectories(output); ImageIO.write(new Robot().createScreenCapture(window.getBounds()),"png",output.resolve(name).toFile()); return;
      }
    }
    void report(String status,String error) {
      try {
        Files.createDirectories(output); var result=new JsonObject(); result.addProperty("status",status); result.addProperty("stage",stage);
        result.addProperty("error",error); result.add("observations",new Gson().toJsonTree(observations));
        Files.writeString(output.resolve("host-acceptance.json"),new GsonBuilder().setPrettyPrinting().create().toJson(result));
      } catch(Exception failure) { failure.printStackTrace(); }
    }
    void finish(boolean passed,String error) {
      if(done) return;
      done=true;
      if(timer!=null) timer.cancel(false); if(query!=null) Disposer.dispose(query);
      for(var entry:originals.entrySet()) if(!project.isDisposed()) {
        if(!entry.getKey().getText().equals(entry.getValue())) WriteCommandAction.runWriteCommandAction(project,() -> entry.getKey().setText(entry.getValue()));
        FileDocumentManager.getInstance().saveDocument(entry.getKey());
      }
      try {
        var nativeFile=output.resolve("native-observation.json");
        if(Files.isRegularFile(nativeFile)) record("actual-null-psi-notifications",JsonParser.parseString(Files.readString(nativeFile)).getAsJsonObject().get("nullPsiNotifications"));
      } catch(Exception failure) { record("native-observer-read-error",failure.toString()); }
      if(passed) {
        try {
          var guard=JsonParser.parseString(Files.readString(output.resolve("stale-navigation-probe.json"))).getAsJsonObject();
          record("sdk-stale-navigation-guard",guard);
          if(!guard.get("passed").getAsBoolean()) { passed=false; error="SDK occurrence navigation accepted a stale result"; }
        } catch(Exception failure) { passed=false; error="Missing SDK stale-navigation guard evidence: "+failure; }
      }
      report(passed?"passed":"failed",error);
      if(passed) DslHostAcceptance.start(project,output,root,() -> {
        if(Boolean.getBoolean("anotherfsm.transportReconnectAcceptance")||Boolean.getBoolean("anotherfsm.backendCrashAcceptance")) BackendReconnectAcceptance.start(project,output,root);
      });
    }
  }
}
