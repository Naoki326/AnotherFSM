package anotherfsm.acceptance;

import anotherfsm.BackendConnection;
import com.google.gson.*;
import com.intellij.ide.DataManager;
import com.intellij.notification.Notification;
import com.intellij.notification.NotificationsManager;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.actionSystem.*;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.command.WriteCommandAction;
import com.intellij.openapi.editor.Document;
import com.intellij.openapi.editor.Editor;
import com.intellij.openapi.fileEditor.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.startup.StartupActivity;
import com.intellij.openapi.util.Disposer;
import com.intellij.openapi.vfs.*;
import com.intellij.openapi.wm.ToolWindowManager;
import com.intellij.ui.content.Content;
import com.intellij.usages.UsageView;
import com.intellij.usages.impl.UsageViewImpl;
import com.jetbrains.rd.ide.model.RdFindUsagesSession;
import com.jetbrains.rdclient.usages.FrontendUsageTarget;
import com.intellij.util.concurrency.AppExecutorUtil;
import java.awt.*;
import java.awt.event.*;
import java.nio.file.*;
import java.util.*;
import java.util.List;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.*;
import javax.imageio.ImageIO;
import javax.swing.*;

/** Independent #34 journey. Registered product actions and real editors/Swing models only.
 * RD is readiness only. SDK/native observer samples corroborate, never replace a user action.
 * No fixture execution, building, assembly loading, source downloads or synthetic screenshots.
 */
public final class ComplexHostAcceptance implements StartupActivity.DumbAware {
  public void runActivity(Project project) {
    if(!Boolean.getBoolean("anotherfsm.complexAcceptance"))return;
    new Journey(project).start();
  }
  private static final String IMPL="AnotherFSM.GoToImplementation", REFS="AnotherFSM.FindReferences";
  private record Place(String file,String anchor,String token) {}
  private static Place at(String file,String anchor,String token) {return new Place(file,anchor,token);}
  private static Place key(String file,String node,String key) {return at(file,"def "+node+"("+key+")",key);}
  private static final class Step {
    final String name;final Runnable enter;final BooleanSupplier verify;final long deadline;
    long begun;boolean started;String status="notRun",detail;
    Step(String name,Runnable enter,BooleanSupplier verify,long deadline) {this.name=name;this.enter=enter;this.verify=verify;this.deadline=deadline;}
  }
  private static final class Journey {
    final Project project;final Path output,root;
    final String caseId=System.getProperty("anotherfsm.acceptance.case","all");
    final Gson gson=new GsonBuilder().setPrettyPrinting().create();
    final List<Step> steps=new ArrayList<>();final List<JsonObject> observations=new ArrayList<>();
    final List<String> gaps=new ArrayList<>();
    final Map<Document,String> originals=new LinkedHashMap<>();final Map<Document,String> edited=new LinkedHashMap<>();
    final Map<Document,String> originalDisk=new LinkedHashMap<>();
    final Set<VirtualFile> initiallyOpen=new HashSet<>();
    final Set<Notification> seen=Collections.newSetFromMap(new IdentityHashMap<>());
    final Set<Content> beforePages=Collections.newSetFromMap(new IdentityHashMap<>());
    final List<Content> createdPages=new ArrayList<>();
    final AtomicBoolean queued=new AtomicBoolean();final long begun=System.nanoTime();
    ScheduledFuture<?> timer;Disposable query;Editor editor;int index,readinessIndex;
    long queryBegun,generation,nativeSeq;boolean inFlight,done,externalAvailable;
    JList<?> chooser;Content current,oldPage,lastNativePage,configuredNativePage;List<String> oldRows;
    int lastNativeDescriptor,configuredNativeDescriptor;JsonArray lastNativeEvidence,configuredNativeEvidence;
    String lastExternalFile;List<String> lastNativeRows=List.of();
    final Set<Integer> nativeDescriptors=new HashSet<>();
    final Map<Document,Set<String>> ownedVersions=new LinkedHashMap<>();
    boolean createdConfiguration,aliasConfigured,isolationChecked;String originalConfiguration;VirtualFile configurationFile;
    Document pendingConfigurationDocument;String pendingConfigurationText;long pendingConfigurationAt;
    final List<Place> readiness=new ArrayList<>();final List<String> readinessStatuses=new ArrayList<>();

    Journey(Project project) {
      this.project=project;
      output=Path.of(Objects.requireNonNull(System.getProperty("anotherfsm.acceptance.dir"),"anotherfsm.acceptance.dir"));
      var supplied=Path.of(Objects.requireNonNull(System.getProperty("anotherfsm.fixture.root"),"anotherfsm.fixture.root")).toAbsolutePath().normalize();
      root=Files.isRegularFile(supplied.resolve("Complex.sln"))?supplied:supplied.resolve("Complex");
      initiallyOpen.addAll(Arrays.asList(FileEditorManager.getInstance(project).getOpenFiles()));
      if(caseId.equals("all")) {
      gaps.add("Project/reference/TFM reload invalidation is not modeled by the linked-source and configuration change tests.");
      gaps.add("External verified-source/Source Link/offline/authorization distinctions cannot be inferred from editor paths or PDBs. Only actual product reason/status and actual navigation are recorded.");
      gaps.add("Decompiler disabled/unaccepted consent/generation failure/cancellation need independent policy setup and observations; this journey does not change host policy or manufacture external failures.");
      }
      specify();
    }
    void require(boolean value,String message) {if(!value)throw new IllegalStateException(message);}
    long since(long nanos) {return (System.nanoTime()-nanos)/1_000_000;}
    void record(String name,Object value) {
      var event=new JsonObject();event.addProperty("step",index);event.addProperty("elapsedMs",since(begun));event.addProperty("name",name);
      event.add("value",gson.toJsonTree(value));observations.add(event);
    }
    void pending(String reason) {steps.get(index).status="pendingVerification";steps.get(index).detail=reason;record("pending-verification",reason);}
    void step(String name,Runnable enter,BooleanSupplier verify) {steps.add(new Step(name,enter,verify,20_000));}
    void once(String name,Runnable enter) {step(name,enter,() -> true);}
    void start() {
      AcceptanceLifecycle.onCancellation(project,() -> finish(false,"Supervisor requested cancellation"),
          () -> done&&edited.isEmpty()&&configurationFile==null);
      report("running",null);
      timer=AppExecutorUtil.getAppScheduledExecutorService().scheduleWithFixedDelay(() -> {
        if(done||!queued.compareAndSet(false,true))return;
        ApplicationManager.getApplication().invokeLater(() -> {
          try {tick();}catch(Throwable failure){finish(false,failure.toString());}finally{queued.set(false);}
        });
      },0,250,TimeUnit.MILLISECONDS);
    }
    VirtualFile file(String relative) {
      var file=LocalFileSystem.getInstance().refreshAndFindFileByNioFile(root.resolve(relative).normalize());
      require(file!=null,"Missing fixture: "+root.resolve(relative));return file;
    }
    Document doc(String relative) {
      var doc=FileDocumentManager.getInstance().getDocument(file(relative));require(doc!=null,"No real document: "+relative);
      if(!originals.containsKey(doc)) {
        originals.put(doc,doc.getText());
        try {originalDisk.put(doc,Files.readString(root.resolve(relative).normalize()));}catch(java.io.IOException failure){throw new IllegalStateException(failure);}
      }
      return doc;
    }
    int offset(Document document,Place place) {
      String text=document.getText();int anchor=text.indexOf(place.anchor());
      require(anchor>=0&&text.indexOf(place.anchor(),anchor+1)<0,"Missing/non-unique fixture anchor: "+place);
      int token=place.anchor().indexOf(place.token());require(token>=0,"Missing token: "+place);return anchor+token;
    }
    void open(Place place) {
      var document=doc(place.file());int offset=offset(document,place);
      editor=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,file(place.file()),offset),true);
      require(editor!=null,"No real editor: "+place.file());editor.getSelectionModel().removeSelection();editor.getCaretModel().moveToOffset(offset);
    }
    Editor selected() {return FileEditorManager.getInstance(project).getSelectedTextEditor();}
    String selectedDescription() {
      var selected=selected();if(selected==null)return "none";
      var file=FileDocumentManager.getInstance().getFile(selected.getDocument());
      return (file==null?"nonphysical":file.getPath())+":"+selected.getCaretModel().getOffset()+":"+selected.getSelectionModel().getSelectedText();
    }
    boolean located(Place place,boolean selection) {
      var selected=selected();return selected!=null&&file(place.file()).equals(FileDocumentManager.getInstance().getFile(selected.getDocument()))
          &&selected.getCaretModel().getOffset()==offset(selected.getDocument(),place)&&(!selection||place.token().equals(selected.getSelectionModel().getSelectedText()));
    }
    boolean declaration(String relative,String className) {
      var selected=selected();if(selected==null)return false;
      var actual=FileDocumentManager.getInstance().getFile(selected.getDocument());
      if(actual==null||(relative!=null&&!file(relative).equals(actual)))return false;
      var document=selected.getDocument();int line=document.getLineNumber(selected.getCaretModel().getOffset());
      String text=document.getText().substring(document.getLineStartOffset(line),document.getLineEndOffset(line));
      if(!text.matches(".*\\bclass\\s+"+className+"\\b.*"))return false;
      record("actual-declaration-editor",Map.of("path",actual.getPath(),"line",line+1,"lineText",text,"sourceIdentityVerified",relative!=null));return true;
    }
    void action(String id) {
      var action=ActionManager.getInstance().getAction(id);require(action!=null,"Missing registered formal action: "+id);
      var context=DataManager.getInstance().getDataContext(editor.getContentComponent());
      require(CommonDataKeys.PROJECT.getData(context)==project&&CommonDataKeys.EDITOR.getData(context)==editor,"Action has no real editor/project context: "+id);
      record("formal-action",Map.of("id",id,"editor",selectedDescription(),"documentVersion",editor.getDocument().getModificationStamp()));
      action.actionPerformed(AnActionEvent.createFromAnAction(action,null,ActionPlaces.EDITOR_POPUP,context));
    }
    void implementation(String name,Place from,String targetFile,String type) {
      step(name,() -> {open(from);action(IMPL);},() -> declaration(targetFile,type));
    }
    Notification[] notifications() {return NotificationsManager.getNotificationsManager().getNotificationsOfType(Notification.class,project);}
    void baselineNotifications() {seen.clear();seen.addAll(Arrays.asList(notifications()));}
    String explanation(Predicate<String> expected) {
      for(var notification:notifications())if(!seen.contains(notification)) {
        String text=notification.getTitle()+" "+notification.getContent();
        if(expected.test(text)){record("actual-visible-notification",text);return text;}
      }return null;
    }
    boolean unfinished(String text) {return text.matches("(?is).*(分析中|尚未完成|analyzing|not ready|后端不可用|请求已取消|无法确认当前结果).*");}
    void rejected(String name,Place place,String expected) {
      step(name,() -> {open(place);baselineNotifications();action(IMPL);},() -> {
        require(located(place,false),"Rejected binding navigated to a guessed target: "+name);
        return explanation(text -> !unfinished(text)&&text.matches("(?is).*(?:"+expected+").*"))!=null;
      });
    }
    List<Component> tree(Component component) {
      var result=new ArrayList<Component>();result.add(component);
      if(component instanceof Container container)for(var child:container.getComponents())result.addAll(tree(child));return result;
    }
    List<String> model(JList<?> list) {
      var rows=new ArrayList<String>();for(int i=0;i<list.getModel().getSize();i++)rows.add(String.valueOf(list.getModel().getElementAt(i)));return rows;
    }
    JList<?> referenceList(Content page) {
      if(page!=null)for(var child:tree(page.getComponent()))if(child instanceof JList<?> list&&"AnotherFSM.references".equals(list.getName()))return list;
      return null;
    }
    List<String> rows(Content page) {var list=referenceList(page);return list==null?List.of():model(list);}
    Content[] pages() {var window=ToolWindowManager.getInstance(project).getToolWindow("AnotherFSM");return window==null?new Content[0]:window.getContentManager().getContents();}
    boolean expired(Content page) {return page!=null&&page.getDisplayName().startsWith("结果已过期");}
    void references(String name,Place from,int count,Predicate<List<String>> expected) {
      step(name,() -> {beforePages.clear();beforePages.addAll(Arrays.asList(pages()));open(from);action(REFS);},() -> {
        for(var page:pages())if(!beforePages.contains(page)&&referenceList(page)!=null&&!expired(page)) {
          var rows=rows(page);if(rows.size()!=count||!expected.test(rows))continue;
          current=page;createdPages.add(page);record("real-reference-model",Map.of("title",page.getDisplayName(),"rows",rows));return true;
        }return false;
      });
    }
    void enter(JList<?> list) {
      require(list!=null&&list.isShowing()&&list.isEnabled()&&list.getSelectedIndex()>=0,"Enter requires a real visible enabled selected list");
      var stroke=KeyStroke.getKeyStroke(KeyEvent.VK_ENTER,0);
      for(int condition:new int[]{JComponent.WHEN_FOCUSED,JComponent.WHEN_ANCESTOR_OF_FOCUSED_COMPONENT,JComponent.WHEN_IN_FOCUSED_WINDOW}) {
        var key=list.getInputMap(condition).get(stroke);var action=key==null?null:list.getActionMap().get(key);
        if(action!=null&&action.isEnabled()) {
          record("component-local Enter",Map.of("component",list.getClass().getName(),"binding",String.valueOf(key),"physicalOsKey",false));
          action.actionPerformed(new ActionEvent(list,ActionEvent.ACTION_PERFORMED,String.valueOf(key)));return;
        }
      }
      var listeners=list.getKeyListeners();require(listeners.length>0,"No public Enter binding/listener on actual popup");
      record("component-local popup Enter",Map.of("listeners",listeners.length,"physicalOsKey",false));
      var pressed=new KeyEvent(list,KeyEvent.KEY_PRESSED,System.currentTimeMillis(),0,KeyEvent.VK_ENTER,KeyEvent.CHAR_UNDEFINED);
      for(var listener:listeners){listener.keyPressed(pressed);if(pressed.isConsumed())break;}
      var released=new KeyEvent(list,KeyEvent.KEY_RELEASED,System.currentTimeMillis(),0,KeyEvent.VK_ENTER,KeyEvent.CHAR_UNDEFINED);
      for(var listener:listeners){listener.keyReleased(released);if(released.isConsumed())break;}
    }
    void chooseHost(String name,Place from,List<String> expected,String selectedHost,String targetFile,String type) {
      step(name+": real host/TFM popup",() -> {open(from);action(IMPL);},() -> {
        for(var window:Window.getWindows())if(window.isShowing())for(var child:tree(window))
          if(child instanceof JList<?> list&&list.isShowing()&&!"AnotherFSM.references".equals(list.getName())) {
            var labels=model(list);
            if(labels.size()==expected.size()&&expected.stream().allMatch(host -> labels.stream().anyMatch(label -> label.contains(host)))) {
              chooser=list;record("real-host-chooser",labels);capture("complex-host-choice-"+index+".png");return true;
            }
          }
        return false;
      });
      step(name+": selected host navigation",() -> {
        var labels=model(chooser);int selected=-1;
        for(int i=0;i<labels.size();i++)if(labels.get(i).contains(selectedHost)){require(selected<0,"Host label not unique: "+selectedHost);selected=i;}
        require(selected>=0,"Host missing from real popup: "+selectedHost);chooser.setSelectedIndex(selected);enter(chooser);
      },() -> declaration(targetFile,type));
    }
    void editBuffer(String relative,UnaryOperator<String> change) {
      var editing=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,file(relative),0),true);
      require(editing!=null,"No real editor for buffer edit: "+relative);var document=doc(relative);String next=change.apply(document.getText());
      require(!next.equals(document.getText()),"No-op fixture edit: "+relative);
      if(!edited.containsKey(document))require(document.getText().equals(originals.get(document))&&!FileDocumentManager.getInstance().isDocumentUnsaved(document),"Refusing to overwrite pre-existing unsaved buffer: "+relative);
      var versions=ownedVersions.computeIfAbsent(document,ignored -> new HashSet<>());versions.add(next);
      if(originalDisk.get(document).contains("\r\n"))versions.add(next.replace("\r\n","\n").replace("\n","\r\n"));
      WriteCommandAction.runWriteCommandAction(project,() -> document.setText(next));edited.put(document,next);
      record("real-editor-buffer-edit",Map.of("file",relative,"documentVersion",document.getModificationStamp(),"savedByTest",false));
    }
    void restore(String relative) {
      var document=doc(relative);String original=originals.get(document);
      require(!edited.containsKey(document)||document.getText().equals(edited.get(document)),"Concurrent user edit; refusing to overwrite: "+relative);
      if(!document.getText().equals(original))WriteCommandAction.runWriteCommandAction(project,() -> document.setText(original));
      restoreDiskIfAutosaved(document,root.resolve(relative));
      edited.remove(document);record("restored-buffer",relative);
    }
    void activate(Content page,boolean keyboard) {
      var window=ToolWindowManager.getInstance(project).getToolWindow("AnotherFSM");
      window.show(() -> {
        try {
          window.getContentManager().setSelectedContent(page);
          var list=referenceList(page);require(list!=null&&list.getModel().getSize()>0,"Missing actual result row");list.setSelectedIndex(0);list.ensureIndexIsVisible(0);
          record("real-reference-activation",Map.of("row",String.valueOf(list.getSelectedValue()),"gesture",keyboard?"Enter":"doubleClick"));
          if(keyboard)enter(list);
          else {var cell=list.getCellBounds(0,0);require(cell!=null,"No actual cell bounds");list.dispatchEvent(new MouseEvent(list,MouseEvent.MOUSE_CLICKED,System.currentTimeMillis(),0,cell.x+3,cell.y+cell.height/2,2,false,MouseEvent.BUTTON1));}
        }catch(Throwable failure){finish(false,failure.toString());}
      });
    }
    void refresh(Content page) {
      var buttons=tree(page.getComponent()).stream().filter(child -> child instanceof JButton button&&"刷新".equals(button.getText())).toList();
      require(buttons.size()==1,"Missing unique real Refresh button");((JButton)buttons.get(0)).doClick();record("real-refresh-click",page.getDisplayName());
    }

    void specify() {
      if(!caseId.equals("all")){specifyFocused();return;}
      readiness.addAll(List.of(key("HostA/sources.fsm","CurrentEntry","CurrentHostKey"),key("HostA/sources.fsm","ProjectEntry","ProjectLookupKey"),
          key("HostA/sources.fsm","BinaryEntry","HostDllKey"),key("HostA/sources.fsm","PackageEntry","LocalPackageKey"),
          key("HostB/host.fsm","DispatchEntry","OtherHostOnlyKey"),key("ProjectNodes/library.fsm","LibraryEntry","ProjectLookupKey"),
          key("StaticWeb/site.fsm","PageEntry","StaticSiteKey"),key("StaticBrowser/browser.fsm","CanvasEntry","BrowserCanvasKey"),
          key("NegativeIdentity/identity.fsm","ControlEntry","IdentityControlKey"),key("../SharedComplex/dual-host.fsm","SharedEntry","SharedHostKey"),
          key("MultiTarget/framework.fsm","FrameworkEntry","FrameworkBindingKey")));
      for(int i=0;i<readiness.size();i++)readinessStatuses.add(i>=readiness.size()-2?"hosts":"type");
      readiness.add(key("StaticWasm/surface.fsm","WasmEntry","WasmSurfaceKey"));readinessStatuses.add("type");
      steps.add(new Step("complete positive SDK/RD readiness (not UI evidence)",() -> {
        require(Files.isRegularFile(root.resolve("Complex.sln")),"Not the controlled Complex fixture root: "+root);
        require(project.getBasePath()!=null&&Path.of(project.getBasePath()).toAbsolutePath().normalize().equals(root),"Wrong project root: "+project.getBasePath());
        checkSentinel();
        record("scope",Map.of("root",root.toString(),"guard","anotherfsm.complexAcceptance","seams",List.of("RD readiness only","registered actions + real editor/Swing","native FindUsages action + completed real session + read-only observer")));
      },() -> {if(!verifySandbox())return false;probeReadiness();return readinessIndex==readiness.size()&&sdkFinished();},180_000));
      implementation("ordinary net8 current-project binding",readiness.get(0),"HostA/Nodes.cs","InvoiceWorker");
      nativeSearch("current-project native C# reverse",null,"InvoiceWorker","CurrentHostKey",List.of("/HostA/sources.fsm"));
      implementation("real contract control survives alias-only impostor",readiness.get(8),"NegativeIdentity/Nodes.cs","ValidWorker");
      implementation("visible project reference navigation",readiness.get(1),"ProjectNodes/Nodes.cs","DependencyWorker");
      nativeSearch("project dependency native C# reverse",null,"DependencyWorker","ProjectLookupKey",List.of("/HostA/sources.fsm","/ProjectNodes/library.fsm"));
      implementation("netstandard library context",key("ProjectNodes/library.fsm","ShelfEntry","NestedLookupKey"),"ProjectNodes/Nodes.cs","ShelfWorker");
      implementation("named attribute alias key overrides constructor",key("HostA/sources.fsm","AliasEntry","NamedOverrideKey"),"ProjectNodes/Nodes.cs","NamedWorker");
      implementation("concrete constructed generic base",key("HostA/sources.fsm","ConstructedEntry","ClosedBufferKey"),"ProjectNodes/Nodes.cs","TextBufferWorker");
      implementation("Web SDK static library",readiness.get(6),"StaticWeb/Nodes.cs","PageWorker");
      implementation("browser static library (not a Blazor/WASM SDK claim)",readiness.get(7),"StaticBrowser/Nodes.cs","CanvasWorker");
      implementation("ordinary net10 context",readiness.get(4),"HostB/Nodes.cs","DispatchWorker");
      implementation("real Microsoft.NET.Sdk.WebAssembly static context",key("StaticWasm/surface.fsm","WasmEntry","WasmSurfaceKey"),"StaticWasm/Nodes.cs","SurfaceWorker");
      for(var external:List.of(new String[]{"BinaryEntry","HostDllKey","BinaryWorker"},new String[]{"PackageEntry","LocalPackageKey","ParcelWorker"},
          new String[]{"LeftEntry","LeftAssemblyKey","CollisionWorker"},new String[]{"RightEntry","RightAssemblyKey","CollisionWorker"})) {
        externalNavigation(external[0],external[1],external[2]);
        nativeSearch("external native C# reverse: "+external[1],"external",external[2],external[1],List.of("/HostA/sources.fsm"));
      }
      rejected("same CLR name across two assemblies is ambiguous",key("HostA/negative.fsm","AmbiguousEntry","AmbiguousAssemblyKey"),"Ambiguous|Conflicting");
      for(var item:List.of(new String[]{"InvisibleHostEntry","OtherHostOnlyKey"},new String[]{"OpenEntry","OpenGenericKey"},
          new String[]{"OpenOuterEntry","OpenOuterKey"},new String[]{"PlainEntry","PlainNotNodeKey"},new String[]{"InvalidEntry","ForbiddenFallbackKey"},
          new String[]{"ClosedNonNodeEntry","ClosedNonNodeKey"},new String[]{"IgnoredEntry","IgnoredCtorKey"}))
        rejected("unavailable/invalid binding: "+item[1],key("HostA/negative.fsm",item[0],item[1]),"Unresolved|No verified|contract unconfirmed|open generic");
      for(var item:List.of(new String[]{"ForeignAttributeEntry","ForeignAttributeKey"},new String[]{"ForeignInterfaceEntry","ForeignInterfaceKey"},new String[]{"ForeignPairEntry","ForeignPairKey"}))
        rejected("foreign C# identity refused: "+item[1],key("NegativeIdentity/identity.fsm",item[0],item[1]),"Unresolved|No verified|contract unconfirmed");
      var shared=key("../SharedComplex/dual-host.fsm","SharedEntry","SharedHostKey");
      chooseHost("linked/shared file HostA",shared,List.of("HostA.csproj","HostB.csproj"),"HostA.csproj","../SharedComplex/SharedBinding.cs","LinkedInvoiceWorker");
      // Same action again must ask again; the previous choice is not shared configuration.
      chooseHost("same linked source asks again and permits HostB",shared,List.of("HostA.csproj","HostB.csproj"),"HostB.csproj","../SharedComplex/SharedBinding.cs","SharedDispatchWorker");
      nativeSearch("shared source native reverse de-duplicates physical key",null,"SharedDispatchWorker","SharedHostKey",List.of("/SharedComplex/dual-host.fsm"));
      var multi=key("MultiTarget/framework.fsm","FrameworkEntry","FrameworkBindingKey");
      chooseHost("multiTFM net8",multi,List.of("net8.0","net10.0"),"net8.0","MultiTarget/Nodes.cs","LegacyRouteWorker");
      chooseHost("multiTFM net10 independent operation",multi,List.of("net8.0","net10.0"),"net10.0","MultiTarget/Nodes.cs","ModernRouteWorker");
      once("add real unsaved uses to linked source",() -> editBuffer("../SharedComplex/dual-host.fsm",text -> text+"\nPulse -> SharedEntry to\nSharedEntry;\n"));
      var sharedDeclaration=at("../SharedComplex/dual-host.fsm","def SharedEntry(SharedHostKey)","SharedEntry");
      references("two physical source positions, not four host duplicates",sharedDeclaration,2,labels -> labels.stream().allMatch(label ->
          label.contains("/SharedComplex/dual-host.fsm")&&label.contains("HostA.csproj")&&label.contains("HostB.csproj"))&&new HashSet<>(labels).size()==2);
      once("retain old result order",() -> {oldPage=current;oldRows=rows(current);capture("complex-grouped-references.png");});
      step("fresh real row navigates to source token",() -> activate(oldPage,true),() -> located(at("../SharedComplex/dual-host.fsm","Pulse -> SharedEntry to","SharedEntry"),true));
      once("change dependent linked source without saving",() -> editBuffer("../SharedComplex/dual-host.fsm",text -> text.replace("Pulse -> SharedEntry to\nSharedEntry;","def Changed(SharedHostKey) {}\nPulse -> SharedEntry to\nChanged;")));
      step("old list actively expires, order retained",() -> {},() -> {
        if(!expired(oldPage))return false;require(rows(oldPage).equals(oldRows),"Expired UI list reordered silently");capture("complex-stale-references.png");return true;
      });
      for(boolean keyboard:List.of(false,true)) {
        var stationary=key("HostA/sources.fsm","CurrentEntry","CurrentHostKey");
        step("stale row rejects "+(keyboard?"Enter":"doubleClick"),() -> {open(stationary);baselineNotifications();activate(oldPage,keyboard);},() -> {
          require(located(stationary,false),"Old list navigated after dependency change");require(rows(oldPage).equals(oldRows),"Stale activation reordered rows");
          return explanation(text -> text.matches("(?is).*(过期|刷新|stale|refresh).*"))!=null;
        });
      }
      step("real Refresh uses changed buffer and preserves host groups",() -> refresh(oldPage),() -> {
        var rows=rows(oldPage);return !expired(oldPage)&&rows.size()==1&&rows.get(0).contains("HostA.csproj")&&rows.get(0).contains("HostB.csproj");
      });
      once("restore linked source",() -> restore("../SharedComplex/dual-host.fsm"));
      step("restore expires changed-buffer results",() -> {},() -> expired(oldPage));
      // A completed, visible empty list is valid here only after the positive two/one-row assertions above.
      step("refresh restored source yields a real completed empty page",() -> refresh(oldPage),() -> !expired(oldPage)&&rows(oldPage).isEmpty());
      configurationCases();
      sourceMatrixCases();
      once("record bounded outstanding AC, never infer all-pass",() -> {
        record("WASM-scope","Microsoft.NET.Sdk.WebAssembly static analysis only; neither execution, AOT nor publish is claimed.");
        record("remaining-acceptance-gaps",gaps);capture("complex-host-acceptance.png");
      });
    }

    void specifyFocused() {
      require(Set.of("generic-native","configuration","source-matrix").contains(caseId),"Unknown independent case: "+caseId);
      if(caseId.equals("source-matrix"))readiness.add(key("../SharedComplex/source-matrix.fsm","MatrixCurrentEntry","MatrixCurrentKey"));
      else readiness.addAll(List.of(key("HostA/sources.fsm","CurrentEntry","CurrentHostKey"),key("ProjectNodes/library.fsm","LibraryEntry","ProjectLookupKey")));
      for(var ignored:readiness)readinessStatuses.add("readiness only");
      steps.add(new Step("independent case source/PSI readiness, not binding evidence",() -> {
        require(Files.isRegularFile(root.resolve("Complex.sln")),"Wrong controlled fixture");checkSentinel();
        record("independent-case-scope",Map.of("case",caseId,"fullMatrixRun",false));
      },() -> {if(!verifySandbox())return false;probeReadiness();return readinessIndex==readiness.size()&&sdkFinished();},90_000));
      if(caseId.equals("configuration"))configurationCases();
      else if(caseId.equals("source-matrix"))sourceMatrixCases();
      else {
        var from=key("HostA/sources.fsm","CurrentEntry","CurrentHostKey");
        var target=projectTarget("../ProjectNodes/ProjectNodes.csproj","Issue34.ProjectLibrary.GenericWarehouse`1+SlotWorker");
        var arguments=new JsonArray();arguments.add(projectTarget("HostA.csproj","Issue34.HostA.InvoiceWorker"));target.add("typeArguments",arguments);
        once("save the independent closed nested generic input",() -> writeConfiguration(configuration(target,1)));
        implementation("closed nested generic implementation",from,"ProjectNodes/Nodes.cs","SlotWorker");
        nativeSearch("closed nested generic native reverse",null,"SlotWorker","CurrentHostKey",List.of("/HostA/sources.fsm"));
        once("restore independent generic input",() -> cleanupConfiguration());
      }
      once("record independent case completion, not whole-support-matrix pass",() -> capture("case-"+caseId+".png"));
    }

    void sourceMatrixCases() {
      var contexts=List.of("MatrixLibrary.csproj / netstandard2.0","MatrixPlain.csproj / net8.0","MatrixPlain.csproj / net10.0",
          "MatrixWeb.csproj / net8.0","MatrixWeb.csproj / net10.0","MatrixWasm.csproj / net8.0","MatrixWasm.csproj / net10.0");
      for(var context:contexts) {
        var current=key("../SharedComplex/source-matrix.fsm","MatrixCurrentEntry","MatrixCurrentKey");
        chooseHost("full source matrix current: "+context,current,contexts,context,"../SharedComplex/SourceMatrix.cs","CurrentWorker");
        nativeSearch("matrix current native reverse: "+context,null,"CurrentWorker","MatrixCurrentKey",List.of("/SharedComplex/source-matrix.fsm"),Set.of("MatrixCurrentKey"),context);
        var project=key("../SharedComplex/source-matrix.fsm","MatrixProjectEntry","MatrixProjectKey");
        chooseHost("full source matrix project: "+context,project,contexts,context,"MatrixDependency/Nodes.cs","ProjectWorker");
        nativeSearch("matrix project native reverse: "+context,null,"ProjectWorker","MatrixProjectKey",List.of("/SharedComplex/source-matrix.fsm"),Set.of("MatrixProjectKey"),context);
        for(var external:List.of(new String[]{"MatrixBinaryEntry","HostDllKey","BinaryWorker"},new String[]{"MatrixPackageEntry","LocalPackageKey","ParcelWorker"})) {
          var place=key("../SharedComplex/source-matrix.fsm",external[0],external[1]);
          chooseExternalMatrixHost("full source matrix external: "+context+" / "+external[1],place,contexts,context,external[2],external[1]);
          nativeSearch("matrix external native reverse: "+context+" / "+external[1],"external",external[2],external[1],List.of("/SharedComplex/source-matrix.fsm"),Set.of(external[1]),context);
        }
      }
    }
    void chooseExternalMatrixHost(String name,Place from,List<String> expected,String selectedHost,String type,String key) {
      step(name+": real consuming host chooser",() -> {externalAvailable=false;lastExternalFile=null;open(from);baselineNotifications();action(IMPL);},() -> {
        for(var window:Window.getWindows())if(window.isShowing())for(var child:tree(window))
          if(child instanceof JList<?> list&&list.isShowing()&&!"AnotherFSM.references".equals(list.getName())) {
            var labels=model(list);if(labels.size()==expected.size()&&expected.stream().allMatch(host -> labels.stream().anyMatch(label -> label.contains(host)))) {
              chooser=list;record("real-matrix-host-chooser",Map.of("origin",key,"selectedContext",selectedHost,"choices",labels));return true;
            }
          }
        return false;
      });
      step(name+": selected consuming external presentation",() -> {
        var labels=model(chooser);int selected=-1;for(int i=0;i<labels.size();i++)if(labels.get(i).contains(selectedHost)){require(selected<0,"Nonunique matrix host");selected=i;}
        require(selected>=0,"Missing matrix host: "+selectedHost);chooser.setSelectedIndex(selected);enter(chooser);
      },() -> observeExternal(from,type,key));
    }

    void restoreDiskIfAutosaved(Document document,Path path) {
      try {
        if(!Files.isRegularFile(path))return;
        String original=originalDisk.get(document),disk=Files.readString(path);
        if(disk.equals(original)) {
          if(FileDocumentManager.getInstance().isDocumentUnsaved(document))FileDocumentManager.getInstance().saveDocument(document);
          return;
        }
        require(ownedVersions.getOrDefault(document,Set.of()).contains(disk),"Concurrent disk edit; refusing to overwrite: "+path);
        FileDocumentManager.getInstance().saveDocument(document);
        require(Files.readString(path).equals(original),"Could not restore auto-saved fixture bytes: "+path);
        record("restored-test-owned-autosave",path.toString());
      }catch(java.io.IOException failure){throw new IllegalStateException(failure);}
    }
    boolean verifySandbox() {
      if(isolationChecked)return true;
      var rider=root.getParent().getParent();var repository=rider.getParent();var box=rider.resolve(".sandbox");
      var expected=new LinkedHashMap<String,Path>();expected.put("idea.config.path",box.resolve("config"));expected.put("idea.system.path",box.resolve("system"));
      expected.put("idea.plugins.path",box.resolve("plugins"));expected.put("idea.log.path",repository.resolve(".scratch/issue-32/sandbox-ide"));
      var actual=new LinkedHashMap<String,String>();
      for(var entry:expected.entrySet()) {
        String value=System.getProperty(entry.getKey());actual.put(entry.getKey(),String.valueOf(value));
        require(value!=null&&Path.of(value).toAbsolutePath().normalize().equals(entry.getValue().toAbsolutePath().normalize()),"Not the controlled sandbox: "+entry.getKey()+"="+value);
      }
      for(var property:List.of("idea.config.import.enabled","idea.settings.sync.enabled","idea.initially.ask.config")) {
        String value=System.getProperty(property);actual.put(property,String.valueOf(value));require("false".equals(value),"Sandbox import/sync is not disabled: "+property);
      }
      try {
        var launcherPath=box.resolve("launcher.json");if(!Files.isRegularFile(launcherPath))return false;
        var launcher=JsonParser.parseString(Files.readString(launcherPath)).getAsJsonObject();
        // Launcher writes its process record immediately after spawn. A stale record is waiting, never isolation proof.
        if(!launcher.has("windowsPid")||launcher.get("windowsPid").getAsLong()!=ProcessHandle.current().pid())return false;
        require(Path.of(launcher.get("fixture").getAsString()).toAbsolutePath().normalize().equals(root.resolve("Complex.sln")),"Launcher fixture differs from actual journey");
        require(Path.of(launcher.get("cwd").getAsString()).toAbsolutePath().normalize().equals(box),"Launcher cwd is not the controlled sandbox");
        var expanded=new ArrayList<String>();for(var item:launcher.getAsJsonArray("expandedArguments"))expanded.add(item.getAsString());
        require(expanded.contains("-Danotherfsm.complexAcceptance=true"),"Launcher did not authorize this independent journey");
        require(expanded.stream().anyMatch(arg -> arg.startsWith("-Danotherfsm.acceptance.dir=")&&Path.of(arg.substring(arg.indexOf('=')+1)).toAbsolutePath().normalize().equals(output.toAbsolutePath().normalize())),"Launcher output differs from actual journey");
        var command=launcher.getAsJsonArray("command");String argumentFile=command.get(1).getAsString();require(argumentFile.startsWith("@"),"Launcher has no explicit argfile");
        byte[] bytes=Files.readAllBytes(Path.of(argumentFile.substring(1)));String digest=HexFormat.of().formatHex(java.security.MessageDigest.getInstance("SHA-256").digest(bytes));
        require(digest.equals(launcher.get("argfileSha256").getAsString()),"Launcher argfile hash mismatch");
        require(Files.isRegularFile(box.resolve("prepared.json")),"Missing prepared sandbox manifest");
        record("verified-live-sandbox-isolation",Map.of("frontendPid",ProcessHandle.current().pid(),"properties",actual,"launcher",launcherPath.toString(),
            "argfile",argumentFile.substring(1),"argfileSha256",digest,"prepared",box.resolve("prepared.json").toString(),"fixture",root.resolve("Complex.sln").toString()));
        isolationChecked=true;return true;
      }catch(java.io.IOException|JsonParseException retry){record("sandbox-launch-record-not-ready",retry.toString());return false;}
      catch(java.security.NoSuchAlgorithmException failure){throw new IllegalStateException(failure);}
    }
    boolean sdkFinished() {
      try {
        var path=output.resolve("complex-sdk-probe.json");if(!Files.isRegularFile(path))return false;
        var parsed=JsonParser.parseString(Files.readString(path));if(!parsed.isJsonObject())return false;
        var report=parsed.getAsJsonObject();String status=report.get("status").getAsString();
        if(caseId.equals("all")) {if(!Set.of("passed","failed","pendingVerification").contains(status))return false;}
        else if(!status.equals("ready")||!report.has("case")||!report.get("case").getAsString().equals(caseId))return false;
        var sample=observer();if(sample==null||sample.get("backendPid").getAsInt()!=report.get("backendPid").getAsInt())return false;
        record("SDK-terminal-readiness-only",Map.of("status",status,"backendPid",report.get("backendPid")));
        // A failed SDK report stays failed in its own seam; it does not silently become a UI assertion.
        return true;
      }catch(java.io.IOException|JsonParseException retry){return false;}
    }
    JsonObject projectTarget(String project,String clr) {
      var target=new JsonObject();target.addProperty("project",project);target.addProperty("clrName",clr);return target;
    }
    String configuration(JsonObject target,int copies) {
      var config=new JsonObject();config.addProperty("version",1);var contexts=new JsonArray();var context=new JsonObject();
      context.addProperty("host","HostA.csproj");context.addProperty("targetFramework","net8.0");var scripts=new JsonArray();scripts.add("sources.fsm");context.add("scripts",scripts);
      var mappings=new JsonArray();for(int i=0;i<copies;i++){var mapping=new JsonObject();mapping.addProperty("key","CurrentHostKey");mapping.add("type",target.deepCopy());mappings.add(mapping);}
      context.add("mappings",mappings);contexts.add(context);config.add("contexts",contexts);return gson.toJson(config);
    }
    void writeConfiguration(String json) {
      try {
        var path=root.resolve("HostA/anotherfsm.json");
        if(configurationFile==null) {
          createdConfiguration=!Files.exists(path);
          if(!createdConfiguration) {
            configurationFile=file("HostA/anotherfsm.json");var existing=FileDocumentManager.getInstance().getDocument(configurationFile);
            require(existing!=null&&!FileDocumentManager.getInstance().isDocumentUnsaved(existing),"Refusing to replace pre-existing unsaved configuration");
            originalConfiguration=Files.readString(path);
          } else {
            // A controlled test input, not plugin-generated configuration. Remove it again in finally.
            Files.writeString(path,"",StandardOpenOption.CREATE_NEW);configurationFile=file("HostA/anotherfsm.json");
          }
        }
        var editing=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,configurationFile,0),true);
        require(editing!=null,"No real configuration editor");var document=doc("HostA/anotherfsm.json");
        if(edited.containsKey(document))require(document.getText().equals(edited.get(document)),"Concurrent configuration edit");
        ownedVersions.computeIfAbsent(document,ignored -> new HashSet<>()).add(json);
        WriteCommandAction.runWriteCommandAction(project,() -> document.setText(json));edited.put(document,json);
        // Adjacent config need not be a project item. Exercise a real saved editor file, not an unshared path-only buffer.
        pendingConfigurationDocument=document;pendingConfigurationText=json;pendingConfigurationAt=System.nanoTime();
        FileDocumentManager.getInstance().saveDocument(document);
        record("configuration-editor-save-requested",Map.of("file",path.toString(),"text",json,"createdByControlledTest",createdConfiguration));
      }catch(java.io.IOException failure){throw new IllegalStateException(failure);}
    }
    boolean configurationSaved() {
      if(pendingConfigurationDocument==null)return true;
      try {
        String disk=Files.readString(root.resolve("HostA/anotherfsm.json")),buffer=pendingConfigurationDocument.getText();
        String expected=pendingConfigurationText.replace("\r\n","\n").stripTrailing();
        if(disk.replace("\r\n","\n").stripTrailing().equals(expected)&&buffer.replace("\r\n","\n").stripTrailing().equals(expected)) {
          ownedVersions.get(pendingConfigurationDocument).add(disk);ownedVersions.get(pendingConfigurationDocument).add(buffer);edited.put(pendingConfigurationDocument,buffer);
          record("real-saved-configuration-editor",Map.of("file",root.resolve("HostA/anotherfsm.json").toString(),"text",disk,"createdByControlledTest",createdConfiguration));
          pendingConfigurationDocument=null;pendingConfigurationText=null;return true;
        }
        require(since(pendingConfigurationAt)<10_000,"Configuration save readback did not match controlled input; no unknown content overwritten");return false;
      }catch(java.io.IOException pendingSave){require(since(pendingConfigurationAt)<10_000,"Configuration save did not complete");return false;}
    }
    void cleanupConfiguration() {
      if(configurationFile==null)return;
      try {
        var document=FileDocumentManager.getInstance().getDocument(configurationFile);var path=root.resolve("HostA/anotherfsm.json");
        String disk=Files.readString(path);
        require(document!=null,"Configuration document unavailable during cleanup");
        require(document.getText().equals(edited.getOrDefault(document,originals.get(document))),"Concurrent configuration buffer edit; refusing cleanup");
        require(ownedVersions.getOrDefault(document,Set.of()).contains(disk)||disk.equals(originalDisk.get(document)),"Concurrent configuration disk edit; refusing cleanup");
        if(createdConfiguration) {
          FileEditorManager.getInstance(project).closeFile(configurationFile);
          ApplicationManager.getApplication().runWriteAction(() -> {try{configurationFile.delete(this);}catch(java.io.IOException failure){throw new IllegalStateException(failure);}});
          require(!Files.exists(path),"Temporary configuration not removed");
        } else {
          WriteCommandAction.runWriteCommandAction(project,() -> document.setText(originals.get(document)));FileDocumentManager.getInstance().saveDocument(document);
          require(Files.readString(path).equals(originalConfiguration),"Original configuration not restored");
        }
        edited.remove(document);record("restored-adjacent-configuration",Map.of("path",path.toString(),"removedTestCreatedFile",createdConfiguration));configurationFile=null;
      }catch(java.io.IOException failure){throw new IllegalStateException(failure);}
    }
    void configurationCases() {
      var from=key("HostA/sources.fsm","CurrentEntry","CurrentHostKey");
      var target=projectTarget("../ProjectNodes/ProjectNodes.csproj","Issue34.ProjectLibrary.Warehouse+UnregisteredShelfWorker");
      once("add source uses before configuration change",() -> editBuffer("../SharedComplex/dual-host.fsm",text -> text+"\nPulse -> SharedEntry to\nSharedEntry;\n"));
      references("retain visible nonempty list before configuration edit",at("../SharedComplex/dual-host.fsm","def SharedEntry(SharedHostKey)","SharedEntry"),2,
          labels -> labels.stream().allMatch(label -> label.contains("HostA.csproj")&&label.contains("HostB.csproj")));
      implementation("configuration native baseline targets original current-project type",from,"HostA/Nodes.cs","InvoiceWorker");
      nativeSearch("configuration baseline native reverse",null,"InvoiceWorker","CurrentHostKey",List.of("/HostA/sources.fsm"));
      once("save explicit override in actual adjacent configuration editor",() -> {
        oldPage=current;oldRows=rows(current);configuredNativePage=lastNativePage;configuredNativeDescriptor=lastNativeDescriptor;
        configuredNativeEvidence=lastNativeEvidence.deepCopy();configuredNativePage.setPinned(true);writeConfiguration(configuration(target,1));
      });
      step("configuration change actively expires old list without reorder",() -> {},() -> expired(oldPage)&&rows(oldPage).equals(oldRows));
      step("configuration change expires actual native query and invalidates old key rows",() -> {},() -> {
        if(!expired(configuredNativePage))return false;var sample=observer();if(sample==null)return false;
        for(var value:sample.getAsJsonArray("descriptors")) {
          var descriptor=value.getAsJsonObject();if(descriptor.get("id").getAsInt()!=configuredNativeDescriptor)continue;
          var rows=new JsonArray();for(var item:descriptor.getAsJsonArray("items")) {
            var row=item.getAsJsonObject();if(row.has("runtimeType")&&!row.get("runtimeType").isJsonNull()&&"AnotherFSM.Rider.FsmOccurrence".equals(row.get("runtimeType").getAsString()))rows.add(row);
          }
          if(rows.size()!=configuredNativeEvidence.size())return false;
          for(int i=0;i<rows.size();i++) {
            var before=configuredNativeEvidence.get(i).getAsJsonObject();var after=rows.get(i).getAsJsonObject();
            require(before.get("id").equals(after.get("id")),"Native config-expired rows silently reordered/replaced");
            if(after.get("valid").getAsBoolean())return false;
          }
          record("real-native-configuration-expiry",Map.of("title",configuredNativePage.getDisplayName(),"rows",rows));capture("complex-config-expired-native.png");return true;
        }
        return false;
      });
      step("old row cannot navigate after configuration change",() -> {open(from);baselineNotifications();activate(oldPage,true);},() -> {
        require(located(from,false),"Configuration-expired list navigated old position");return explanation(text -> text.matches("(?is).*(过期|刷新|stale|refresh).*"))!=null;
      });
      step("explicit mapping replaces current attribute key and visibly reports configuration source",() -> {
        open(from);baselineNotifications();action(IMPL);
      },() -> declaration("ProjectNodes/Nodes.cs","UnregisteredShelfWorker")
          &&explanation(text -> text.contains("Explicit mapping")&&text.contains("anotherfsm.json"))!=null);
      nativeSearch("configured target native reverse uses overridden key",null,"UnregisteredShelfWorker","CurrentHostKey",List.of("/HostA/sources.fsm"));
      once("save invalid explicit target (must not fall back)",() -> writeConfiguration(configuration(projectTarget("../ProjectNodes/ProjectNodes.csproj","Issue34.ProjectLibrary.DoesNotExist"),1)));
      rejected("invalid explicit target refuses attribute fallback",from,"Explicit mapping.*(unavailable|unconfirmed|invalid)");
      once("save duplicate identical explicit mappings",() -> writeConfiguration(configuration(target,2)));
      rejected("duplicate explicit entries remain ambiguous",from,"duplicate|Ambiguous|conflicting explicit");
      for(var clr:List.of("Issue34.ProjectLibrary.BufferWorker`1","Issue34.ProjectLibrary.GenericWarehouse`1+SlotWorker")) {
        var generic=projectTarget("../ProjectNodes/ProjectNodes.csproj",clr);var arguments=new JsonArray();arguments.add(projectTarget("HostA.csproj","Issue34.HostA.InvoiceWorker"));generic.add("typeArguments",arguments);
        once("save closed generic mapping: "+clr,() -> writeConfiguration(configuration(generic,1)));
        String type=clr.contains("SlotWorker")?"SlotWorker":"BufferWorker";
        implementation("closed generic mapping navigates declaration: "+clr,from,"ProjectNodes/Nodes.cs",type);
        nativeSearch("closed generic native reverse: "+clr,null,type,"CurrentHostKey",List.of("/HostA/sources.fsm"));
      }
      // Full external assembly identity is obtained from the actual SDK report, never guessed from a display name.
      step("save explicit extern-alias target using actual SDK assembly identity",() -> {
        aliasConfigured=false;
        try {
          var sdk=JsonParser.parseString(Files.readString(output.resolve("complex-sdk-probe.json"))).getAsJsonObject();String assembly=null;
          for(var value:sdk.getAsJsonArray("observations")) {
            var observation=value.getAsJsonObject();if(!observation.has("name")||!"alias-definitions".equals(observation.get("name").getAsString()))continue;
            for(var entry:observation.getAsJsonArray("definitions")) {
              var definition=entry.getAsJsonObject();if(definition.get("alias").getAsString().equals("LeftAssembly"))assembly=definition.get("assembly").getAsString();
            }
          }
          if(assembly==null){pending("Actual full assembly identity unavailable; explicit extern-alias UI scenario not attempted");return;}
          var alias=new JsonObject();alias.addProperty("assembly",assembly);alias.addProperty("clrName","Issue34.SameNames.CollisionWorker");alias.addProperty("alias","LeftAssembly");writeConfiguration(configuration(alias,1));aliasConfigured=true;
        }catch(java.io.IOException failure){throw new IllegalStateException(failure);}
      },() -> true);
      // Alias-specific external target may be unavailable in the product. Never relabel it as verified source.
      step("explicit extern alias navigation via formal action",() -> {
        externalAvailable=false;if(!aliasConfigured){pending("No actual assembly identity; explicit-alias action not invoked");return;}open(from);baselineNotifications();action(IMPL);
      },() -> {
        return !aliasConfigured||observeExternal(from,"CollisionWorker","CurrentHostKey");
      });
      nativeSearch("explicit extern alias native reverse","external","CollisionWorker","CurrentHostKey",List.of("/HostA/sources.fsm"),Set.of("CurrentHostKey","LeftAssemblyKey"));
      once("save config-relative pre-registered Module file",() -> {
        var config=new JsonObject();config.addProperty("version",1);var contexts=new JsonArray();var context=new JsonObject();
        context.addProperty("host","HostA.csproj");var scripts=new JsonArray();scripts.add("preset.fsm");context.add("scripts",scripts);
        var mappings=new JsonArray();var mapping=new JsonObject();mapping.addProperty("key","CurrentHostKey");mapping.add("type",target.deepCopy());mappings.add(mapping);context.add("mappings",mappings);
        var modules=new JsonArray();var module=new JsonObject();module.addProperty("name","Configured");module.addProperty("file","../../SharedComplex/registered-module.fsm");
        modules.add(module);var support=new JsonObject();support.addProperty("name","Support");support.addProperty("file","../../SharedComplex/registered-support.fsm");modules.add(support);
        context.add("modules",modules);contexts.add(context);config.add("contexts",contexts);writeConfiguration(gson.toJson(config));
      });
      step("registered Module navigates to actual config-relative source without import",() -> {
        open(at("HostA/preset.fsm","use Configured as ConfiguredFlow","Configured"));action("AnotherFSM.GoToModuleDefinition");
      },() -> located(at("../SharedComplex/registered-module.fsm","module Configured","Configured"),true));
      implementation("registered Module Input retains consuming C# association",at("../SharedComplex/registered-module.fsm","input Entry","Entry"),"HostA/Nodes.cs","InvoiceWorker");
      implementation("root-file override applies to its own factory declaration",key("HostA/preset.fsm","Finish","CurrentHostKey"),"ProjectNodes/Nodes.cs","UnregisteredShelfWorker");
      implementation("root-file override does not leak through an Input reference into another factory source",at("HostA/preset.fsm","Enter -> Finish to ConfiguredFlow.Entry","Entry"),"HostA/Nodes.cs","InvoiceWorker");
      once("save case-sensitive explicit host mapping and unsaved lowercase key",() -> {
        var config=JsonParser.parseString(configuration(target,1)).getAsJsonObject();config.getAsJsonArray("contexts").get(0).getAsJsonObject().addProperty("keyComparison","Ordinal");
        writeConfiguration(gson.toJson(config));editBuffer("HostA/sources.fsm",text -> text.replace("CurrentEntry(CurrentHostKey)","CurrentEntry(currenthostkey)"));
      });
      rejected("Ordinal key comparison does not borrow a differently cased default or mapping",key("HostA/sources.fsm","CurrentEntry","currenthostkey"),"Unresolved|No verified");
      once("save case-insensitive explicit host mapping",() -> {
        var config=JsonParser.parseString(configuration(target,1)).getAsJsonObject();config.getAsJsonArray("contexts").get(0).getAsJsonObject().addProperty("keyComparison","OrdinalIgnoreCase");writeConfiguration(gson.toJson(config));
      });
      implementation("OrdinalIgnoreCase resolves the same unsaved lowercase key",key("HostA/sources.fsm","CurrentEntry","currenthostkey"),"ProjectNodes/Nodes.cs","UnregisteredShelfWorker");
      once("restore adjacent configuration and source buffers",() -> {cleanupConfiguration();restore("HostA/sources.fsm");restore("../SharedComplex/dual-host.fsm");});
      var restoredHost=new long[]{-1,-1,0};
      step("restored inputs settle at independently observed stable host epochs",() -> {},() -> {
        var sample=observer();if(sample==null)return false;
        long sequence=sample.get("seq").getAsLong(),epoch=sample.get("epoch").getAsLong();
        if(sequence!=restoredHost[0]) {
          restoredHost[2]=epoch==restoredHost[1]?restoredHost[2]+1:1;restoredHost[0]=sequence;restoredHost[1]=epoch;
        }
        return restoredHost[2]>=4&&!Files.exists(root.resolve("HostA/anotherfsm.json"))
            &&doc("HostA/sources.fsm").getText().equals(originals.get(doc("HostA/sources.fsm")));
      });
      implementation("configuration restoration returns original binding",from,"HostA/Nodes.cs","InvoiceWorker");
      nativeSearch("restored current-project native reverse",null,"InvoiceWorker","CurrentHostKey",List.of("/HostA/sources.fsm"));
    }

    void externalNavigation(String node,String key,String type) {
      var from=key("HostA/sources.fsm",node,key);
      step("external origin navigation / conservative presentation: "+key,() -> {
        externalAvailable=false;lastExternalFile=null;open(from);baselineNotifications();action(IMPL);
      },() -> observeExternal(from,type,key));
    }
    boolean observeExternal(Place from,String type,String key) {
      // This must be the product's visible label, not our own classification of a path or an RD dispatch.
      String reason=explanation(text -> !unfinished(text)&&text.contains(type)&&text.matches("(?is).*(source match unverified|源码.*未验证|未验证.*源码).*"));
      if(reason!=null&&declaration(null,type)) {
        externalAvailable=true;lastExternalFile=FileDocumentManager.getInstance().getFile(selected().getDocument()).getPath();
        String presentation=reason.contains("Decompiled code")?"decompiled":reason.contains("Metadata view")?"metadata":"unverified external presentation";
        record("external-navigation-and-visible-origin",Map.of("key",key,"path",lastExternalFile,"visibleReason",reason,"presentation",presentation,"sourceMatch","unverified"));
        capture("complex-external-"+key+"-"+index+".png");return true;
      }
      if(reason!=null&&!reason.toLowerCase(Locale.ROOT).contains("navigation dispatched")&&located(from,false)) {
        record("visible-external-metadata-fallback",Map.of("key",key,"reason",reason,"sourceMatch","unverified","actualDeclarationOpened",false));
        record("conservative-presentation-accepted","Actual metadata fallback under observed host policy; not a claim that a declaration editor opened. Native reverse is tested separately from a real C# type use.");return true;
      }
      // A prior product revision may expose only this exact refusal. It is evidence of a gap, never a pass for binding retention.
      var refusal=explanation(text -> !unfinished(text)&&text.contains("Current-project source unavailable"));
      if(refusal!=null&&located(from,false)){pending("External determined binding presentation not exposed: "+refusal);return true;}
      return false;
    }
    JsonObject observer() {
      try {var path=output.resolve("native-observation.json");if(!Files.isRegularFile(path))return null;var parsed=JsonParser.parseString(Files.readString(path));return parsed.isJsonObject()?parsed.getAsJsonObject():null;}
      catch(java.io.IOException|JsonParseException retry){return null;}
    }
    void nativeSearch(String name,String external,String type,String key,List<String> requiredPaths) {
      nativeSearch(name,external,type,key,requiredPaths,Set.of(key));
    }
    void nativeSearch(String name,String external,String type,String key,List<String> requiredPaths,Set<String> acceptedKeys) {
      nativeSearch(name,external,type,key,requiredPaths,acceptedKeys,null);
    }
    void nativeSearch(String name,String external,String type,String key,List<String> requiredPaths,Set<String> acceptedKeys,String requiredHost) {
      step(name,() -> {
        if(external!=null&&!externalAvailable) {
          String qualified=switch(type) {
            case "BinaryWorker" -> "Issue34.BinaryLibrary.BinaryWorker";
            case "ParcelWorker" -> "Issue34.PackageLibrary.ParcelWorker";
            case "CollisionWorker" -> ("RightAssemblyKey".equals(key)?"RightAssembly":"LeftAssembly")+"::Issue34.SameNames.CollisionWorker";
            default -> throw new IllegalStateException("No controlled C# usage witness for "+type);
          };
          var witness=at("HostA/Nodes.cs","typeof("+qualified+")",type);open(witness);
          require(located(witness,false),"Native FindUsages lost the actual C# type use");
          record("native-from-real-CSharp-use",Map.of("file",witness.file(),"anchor",witness.anchor(),"type",type,"reason","Host external presentation remained metadata-only; this is an actual C# usage, not an SDK substitute"));
        } else require(declaration(null,type),"Native FindUsages must start at the actual navigated declaration: "+type);
        editor=selected();var baseline=observer();nativeSeq=baseline==null?-1:baseline.get("seq").getAsLong();nativeDescriptors.clear();
        if(baseline!=null)for(var value:baseline.getAsJsonArray("descriptors"))nativeDescriptors.add(value.getAsJsonObject().get("id").getAsInt());
        action("FindUsages");
      },() -> {
        if("pendingVerification".equals(steps.get(index).status))return true;
        var window=ToolWindowManager.getInstance(project).getToolWindow("Find");
        if(window==null||window.getContentManager().getSelectedContent()==null)return false;
        var page=window.getContentManager().getSelectedContent();if(!page.getDisplayName().contains(type))return false;
        var view=UsageView.USAGE_VIEW_KEY.getData(DataManager.getInstance().getDataContext(page.getComponent()));if(!(view instanceof UsageViewImpl nativeView))return false;
        var sessions=Collections.newSetFromMap(new IdentityHashMap<RdFindUsagesSession,Boolean>());
        for(var target:nativeView.getTargets())if(target instanceof FrontendUsageTarget frontend)sessions.add(frontend.getSession());
        if(sessions.size()!=1||!Boolean.TRUE.equals(sessions.iterator().next().isSearchComplete().getValueOrNull()))return false;
        var sample=observer();if(sample==null||sample.get("seq").getAsLong()<=nativeSeq)return false;
        for(var item:sample.getAsJsonArray("descriptors")) {
          var descriptor=item.getAsJsonObject();if(nativeDescriptors.contains(descriptor.get("id").getAsInt())||!descriptor.get("title").getAsString().contains(type)||descriptor.get("updating").getAsBoolean()||descriptor.get("busy").getAsBoolean())continue;
          var positions=new HashSet<String>();var files=new ArrayList<String>();var evidence=new JsonArray();
          for(var value:descriptor.getAsJsonArray("items")) {
            var row=value.getAsJsonObject();if(!row.has("runtimeType")||row.get("runtimeType").isJsonNull()||!"AnotherFSM.Rider.FsmOccurrence".equals(row.get("runtimeType").getAsString()))continue;
            require(row.get("valid").getAsBoolean(),"Completed native search exposed an invalid DSL occurrence");
            require(acceptedKeys.contains(row.get("currentSlice").getAsString()),"Native reverse search returned another key / Node name: "+row);
            String path=row.get("file").getAsString();
            if(requiredHost!=null&&path.endsWith("/SharedComplex/source-matrix.fsm"))
              require(row.get("text").getAsString().contains(requiredHost),"Native matrix row lost its actual consuming host/TFM group: "+requiredHost+" / "+row);
            require(positions.add(path+":"+row.get("start")+":"+row.get("length")),"Native reverse search duplicated physical source position");
            files.add(path);evidence.add(row);
          }
          if(!requiredPaths.stream().allMatch(path -> files.stream().anyMatch(file -> file.endsWith(path))))continue;
          boolean primaryKey=false;for(var value:evidence)if(key.equals(value.getAsJsonObject().get("currentSlice").getAsString()))primaryKey=true;
          if(!primaryKey)continue;
          record("completed-real-native-search",Map.of("action","FindUsages","type",type,"key",key,"observerSequence",sample.get("seq"),"descriptorId",descriptor.get("id"),"rows",evidence));
          lastNativeRows=files;lastNativePage=page;lastNativeDescriptor=descriptor.get("id").getAsInt();lastNativeEvidence=evidence.deepCopy();
          for(var component:tree(page.getComponent()))if(component instanceof JTree resultTree)for(int row=0;row<resultTree.getRowCount()&&row<256;row++)resultTree.expandRow(row);
          capture("complex-native-"+key+".png");return true;
        }return false;
      });
    }
    void probeReadiness() {
      if(readinessIndex==readiness.size())return;
      if(inFlight){if(since(queryBegun)>10_000){generation++;inFlight=false;if(query!=null)Disposer.dispose(query);record("readiness-request-timeout","not a zero-result pass");}return;}
      var place=readiness.get(readinessIndex);var document=doc(place.file());var request=new JsonObject();
      request.addProperty("action","resolve");request.addProperty("implementation",true);request.addProperty("path",file(place.file()).getPath());request.addProperty("text",document.getText());
      request.addProperty("offset",offset(document,place));request.addProperty("documentVersion",document.getModificationStamp());request.addProperty("requestId",UUID.randomUUID().toString());
      long own=++generation;inFlight=true;queryBegun=System.nanoTime();record("RD-readiness-request-only",request);
      query=BackendConnection.get(project).query(request.toString(),raw -> {
        if(done||own!=generation)return;
        try {
          inFlight=false;var reply=JsonParser.parseString(raw).getAsJsonObject();record("RD-readiness-reply-only",reply);
          if(!reply.has("status")||Set.of("analyzing","stale").contains(reply.get("status").getAsString())||!sdkFinished())return;
          // Binding correctness belongs to the formal action assertions, not the host readiness gate.
          require(request.get("requestId").equals(reply.get("requestId"))&&request.get("documentVersion").equals(reply.get("documentVersion")),"RD readiness identity mismatch");
          require(reply.has("session")&&!reply.get("session").getAsString().isBlank()&&reply.get("contextVersion").getAsLong()>=0,"RD context identity missing");
          readinessIndex++;
        } catch(Throwable failure){finish(false,failure.toString());}
      },failure -> {if(!done&&own==generation){inFlight=false;record("RD-readiness-unavailable",failure.toString());}});
    }
    void checkSentinel() {
      String marker=System.getenv("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");if(marker==null||marker.isBlank())return;
      var path=Path.of(marker);require(path.isAbsolute(),"Execution marker must be absolute");
      if(Files.exists(path)) {
        try {record("forbidden-business-marker",Map.of("path",marker,"contents",Files.readString(path)));}catch(Exception failure){record("marker-read-error",failure.toString());}
        throw new IllegalStateException("Instrumented business marker exists (possibly predates run); retained, never deleted: "+marker);
      }
    }
    void tick() {
      if(done)return;require(!project.isDisposed(),"Project disposed during complex journey");checkSentinel();
      if(!configurationSaved())return;
      if(index==steps.size()){finish(true,null);return;}require(since(begun)<900_000,"Complex journey total deadline exceeded");
      var step=steps.get(index);
      if(!step.started){step.started=true;step.begun=System.nanoTime();step.status="running";record("begin",step.name);report("running",null);step.enter.run();return;}
      if(step.verify.getAsBoolean()) {
        if("running".equals(step.status))step.status="passed";record(step.status,step.name);index++;report("running",null);
      } else if(since(step.begun)>step.deadline) {
        if(index==0)step.status="pendingVerification";
        throw new IllegalStateException("Deadline: "+step.name+"; selected="+selectedDescription()+"; currentRows="+rows(current)+"; no cause inferred");
      }
    }
    void capture(String name) {
      try {
        var window=com.intellij.openapi.wm.WindowManager.getInstance().getFrame(project);
        require(window!=null&&window.isShowing()&&window.getWidth()>500,"No visible real Rider project frame");
        Files.createDirectories(output);ImageIO.write(new Robot().createScreenCapture(window.getBounds()),"png",output.resolve(name).toFile());
        record("real-window-screenshot",Map.of("file",name,"bounds",window.getBounds().toString()));
      }catch(Exception failure){throw new IllegalStateException("Real screenshot unavailable",failure);}
    }
    void report(String status,String error) {
      try {
        Files.createDirectories(output);var report=new JsonObject();report.addProperty("status",status);report.addProperty("case",caseId);report.addProperty("scope",caseId.equals("all")?"full journey":"independent case, not whole matrix");report.addProperty("error",error);report.addProperty("step",index);report.addProperty("elapsedMs",since(begun));
        report.addProperty("seam","registered formal Rider actions + actual editors/Swing/native completed session; RD readiness only");
        var coverage=new JsonArray();for(var step:steps){var item=new JsonObject();item.addProperty("name",step.name);item.addProperty("status",step.status);item.addProperty("detail",step.detail);coverage.add(item);}
        report.add("coverage",coverage);report.add("observations",gson.toJsonTree(observations));report.add("pendingVerification",gson.toJsonTree(gaps));
        String marker=System.getenv("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");var sentinel=new JsonObject();sentinel.addProperty("path",marker);sentinel.addProperty("configured",marker!=null&&!marker.isBlank());
        sentinel.addProperty("claim","instrumented fixture paths only; no marker configuration means safety remains pending, not passed");report.add("sentinel",sentinel);
        report.add("outOfScope",gson.toJsonTree(List.of("#33 complete DSL regression", "#35 completion", "#36 performance", "runtime workflow execution")));
        Files.writeString(output.resolve("complex-host-acceptance.json"),gson.toJson(report));
      }catch(Exception failure){failure.printStackTrace();}
    }
    void finish(boolean passed,String error) {
      if(done)return;done=true;generation++;if(timer!=null)timer.cancel(false);if(query!=null)Disposer.dispose(query);
      boolean incomplete=index==0&&"pendingVerification".equals(steps.get(0).status);
      if(!passed)record("actual-failure-notifications",Arrays.stream(notifications()).filter(item -> (item.getTitle()+item.getContent()).contains("AnotherFSM")).map(item -> item.getTitle()+" "+item.getContent()).toList());
      if(!passed){if(index<steps.size()){steps.get(index).status=incomplete?"pendingVerification":"failed";steps.get(index).detail=error;}try{capture("complex-host-failure.png");}catch(Throwable failure){record("screenshot-error",failure.toString());}}
      var cleanupErrors=new ArrayList<String>();
      // Restore independent test-owned buffers even if another buffer has a concurrent edit.
      for(var entry:new ArrayList<>(edited.entrySet())) {
        var document=entry.getKey();var actual=FileDocumentManager.getInstance().getFile(document);
        if(configurationFile!=null&&configurationFile.equals(actual))continue;
        try {
          require(document.getText().equals(entry.getValue()),"Concurrent edit during cleanup; refusing overwrite: "+actual);
          String original=originals.get(document);if(!document.getText().equals(original))WriteCommandAction.runWriteCommandAction(project,() -> document.setText(original));
          if(actual!=null)restoreDiskIfAutosaved(document,Path.of(actual.getPath()));edited.remove(document);
        }catch(Throwable failure){cleanupErrors.add(failure.toString());}
      }
      try {cleanupConfiguration();}catch(Throwable failure){cleanupErrors.add(failure.toString());}
      if(configuredNativePage!=null)configuredNativePage.setPinned(false);
      try {
        for(var opened:FileEditorManager.getInstance(project).getOpenFiles())if(!initiallyOpen.contains(opened)) {
          var document=FileDocumentManager.getInstance().getDocument(opened);
          if(document!=null&&originals.containsKey(document)&&document.getText().equals(originals.get(document)))FileEditorManager.getInstance(project).closeFile(opened);
        }
      }catch(Throwable failure){cleanupErrors.add(failure.toString());}
      try {checkSentinel();}catch(Throwable failure){cleanupErrors.add(failure.toString());}
      if(!cleanupErrors.isEmpty()){passed=false;incomplete=false;error=String.valueOf(error)+"; cleanup: "+cleanupErrors;record("cleanup-error",cleanupErrors);}
      String marker=System.getenv("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");if(marker==null||marker.isBlank())gaps.add("ANOTHERFSM_COMPLEX_EXECUTION_MARKER not configured; instrumented business entry non-execution not verified by a marker.");
      boolean pending=!gaps.isEmpty()||steps.stream().anyMatch(step -> "pendingVerification".equals(step.status));
      report(!passed?(incomplete?"pendingVerification":"failed"):pending?"partial":"passed",error);
    }
  }
}
