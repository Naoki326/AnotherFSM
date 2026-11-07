package anotherfsm.acceptance;

import anotherfsm.BackendConnection;
import com.google.gson.*;
import com.intellij.ide.DataManager;
import com.intellij.notification.Notification;
import com.intellij.notification.NotificationsManager;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.actionSystem.*;
import com.intellij.openapi.application.ApplicationInfo;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.command.WriteCommandAction;
import com.intellij.openapi.editor.Document;
import com.intellij.openapi.editor.Editor;
import com.intellij.openapi.fileEditor.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.util.Disposer;
import com.intellij.openapi.vfs.*;
import com.intellij.openapi.wm.ToolWindowManager;
import com.intellij.ui.content.Content;
import com.intellij.usages.UsageView;
import com.intellij.usages.impl.UsageViewImpl;
import com.jetbrains.rd.ide.model.RdFindUsagesSession;
import com.jetbrains.rdclient.usages.FrontendUsageTarget;
import com.intellij.testFramework.LightVirtualFile;
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

/**
 * Issue #33 red specification. Runs AFTER the #32 journey, before reconnect fault injection.
 * All behavior assertions observe registered actions, actual editors, notifications and public
 * Swing models. RD is readiness/real-transport evidence only, never a substitute for a UI action.
 * No test service, product internals, runtime graph building or business execution.
 */
public final class DslHostAcceptance {
  private DslHostAcceptance() {}
  public static void start(Project project, Path output, Path root, Runnable onSuccess) {
    new Journey(project, output, root, onSuccess).start();
  }

  private static final String GRAPH="Graphs/Flow/graph.fsm", CHECKER="Graphs/Flow/Checker.fsm";
  private static final String UNOPENED="Graphs/Flow/unopened.FSMScript";
  private static final String DECL="AnotherFSM.GoToDeclaration", IMPL="AnotherFSM.GoToImplementation";
  private static final String REFS="AnotherFSM.FindReferences", TEMPLATE="AnotherFSM.FindTemplateReferences";
  private static final String MARKER="graph-business-execution.marker";
  private record Place(String file, String anchor, String token) {}
  private static Place at(String file,String anchor,String token) { return new Place(file,anchor,token); }
  private static final class Step {
    final String name; final Runnable enter; final BooleanSupplier verify;
    final long limit; boolean started; long begun; String status="pending";
    Step(String name,Runnable enter,BooleanSupplier verify,long limit) {
      this.name=name;this.enter=enter;this.verify=verify;this.limit=limit;
    }
  }

  private static final class Journey {
    final Project project; final Path output,root; final Runnable onSuccess;
    final Gson gson=new GsonBuilder().setPrettyPrinting().create();
    final List<Step> steps=new ArrayList<>(); final List<JsonObject> events=new ArrayList<>();
    final Map<Document,String> originals=new LinkedHashMap<>();
    final AtomicBoolean queued=new AtomicBoolean();
    final Set<Notification> seen=Collections.newSetFromMap(new IdentityHashMap<>());
    final Set<Content> beforeContents=Collections.newSetFromMap(new IdentityHashMap<>());
    final List<Content> pages=new ArrayList<>();
    final long begun=System.nanoTime(); ScheduledFuture<?> timer;
    Editor editor; int index; boolean done,inFlight,ready; Disposable query;
    long queryStart,generation; String notificationEvidence;
    Content current,oldPage,otherPage; List<String> oldRows,otherRows;
    Place stationary; LightVirtualFile untitled;

    Journey(Project project,Path output,Path root,Runnable onSuccess) {
      this.project=project;this.output=output;this.root=root;this.onSuccess=onSuccess;specify();
    }
    void start() {
      AcceptanceLifecycle.onCancellation(project,() -> finish(false,"Supervisor requested cancellation"),
          () -> done&&originals.entrySet().stream().allMatch(entry -> entry.getKey().getText().equals(entry.getValue())));
      report("running",null);
      timer=AppExecutorUtil.getAppScheduledExecutorService().scheduleWithFixedDelay(() -> {
        if(done||!queued.compareAndSet(false,true))return;
        ApplicationManager.getApplication().invokeLater(() -> {
          try { tick(); } catch(Throwable failure) { finish(false,failure.toString()); }
          finally { queued.set(false); }
        });
      },0,250,TimeUnit.MILLISECONDS);
    }
    long since(long time) { return (System.nanoTime()-time)/1_000_000; }
    void require(boolean value,String message) { if(!value)throw new IllegalStateException(message); }
    List<Path> sentinelPaths() {
      return List.of(output.resolve(MARKER),root.resolve("../../.scratch/issue-33/host/"+MARKER).normalize());
    }
    void checkSentinels() {
      for(var path:sentinelPaths())if(Files.exists(path)) {
        try {record("forbidden-business-execution-marker",Map.of("path",path.toString(),"contents",Files.readString(path)));}
        catch(java.io.IOException failure) {record("sentinel-read-failure",failure.toString());}
        throw new IllegalStateException("Controlled Graphs business code executed (or marker predates run): "+path+"; evidence retained, never auto-deleted");
      }
    }
    void recordSentinelScope() {
      checkSentinels();
      record("business-execution-sentinel-scope",Map.of(
          "markers",sentinelPaths().stream().map(Path::toString).toList(),
          "sourceFiles",List.of("rider/fixtures/Graphs/GraphExecutionSentinel.cs","rider/fixtures/Graphs/Nodes.cs"),
          "guardedPaths",List.of("GraphFactory.ctor","GraphFactory.Current.get","GraphFactory.CreateNode","GraphFactory.GetNodeType","GraphFactory.GetNodeFeatureName",
              "GraphWorker.ctor","GraphWorker.BusinessValue.get","GraphWorker.RuntimeKey.get","GraphWorker.ExecuteEnumerable","BareWorker.ctor","BareWorker.ExecuteEnumerable","IdentityUse.Node.get"),
          "explicitFixtureBuild","Authorized separately; compiling/restoring the controlled fixture is not IDE analysis and does not intentionally invoke these entries",
          "claimLimit","Marker absence observes these instrumented fixture paths only; not proof that every business execution/download/credential boundary is safe"));
    }
    void record(String name,Object value) {
      var item=new JsonObject();item.addProperty("elapsedMs",since(begun));item.addProperty("step",index);
      item.addProperty("name",name);item.add("value",gson.toJsonTree(value));events.add(item);
    }
    void step(String name,Runnable enter,BooleanSupplier verify) { steps.add(new Step(name,enter,verify,15_000)); }
    void once(String name,Runnable action) { step(name,action,() -> true); }
    VirtualFile file(String relative) {
      var value=LocalFileSystem.getInstance().refreshAndFindFileByNioFile(root.resolve(relative));
      require(value!=null,"Missing fixture "+relative);return value;
    }
    Document doc(String relative) {
      var value=FileDocumentManager.getInstance().getDocument(file(relative));
      require(value!=null,"No actual document "+relative);originals.putIfAbsent(value,value.getText());return value;
    }
    int offset(Document document,Place place) {
      int anchor=document.getText().indexOf(place.anchor());
      require(anchor>=0&&document.getText().indexOf(place.anchor(),anchor+1)<0,"Non-unique/missing fixture anchor "+place);
      // A component token is not an arbitrary substring: Entry in LaunchEntry is not Left.Entry.
      // Keep the first actual token/component; deliberately repeated Signal has a separate shorter Node anchor.
      int token=-1;
      for(int candidate=place.anchor().indexOf(place.token());candidate>=0;candidate=place.anchor().indexOf(place.token(),candidate+1)) {
        int end=candidate+place.token().length();
        boolean left=candidate==0||!(Character.isLetterOrDigit(place.anchor().charAt(candidate-1))||place.anchor().charAt(candidate-1)=='_');
        boolean right=end==place.anchor().length()||!(Character.isLetterOrDigit(place.anchor().charAt(end))||place.anchor().charAt(end)=='_');
        if(left&&right){token=candidate;break;}
      }
      require(token>=0,"Token/component absent from anchor "+place);return anchor+token;
    }
    void open(Place place) {
      var document=doc(place.file());int offset=offset(document,place);
      editor=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,file(place.file()),offset),true);
      require(editor!=null,"No real editor "+place.file());
      editor.getSelectionModel().removeSelection();editor.getCaretModel().moveToOffset(offset);
    }
    Editor selected() { return FileEditorManager.getInstance(project).getSelectedTextEditor(); }
    boolean located(Place place,boolean selection) {
      var target=selected();if(target==null)return false;
      var actual=FileDocumentManager.getInstance().getFile(target.getDocument());if(!file(place.file()).equals(actual))return false;
      return target.getCaretModel().getOffset()==offset(target.getDocument(),place)
          &&(!selection||place.token().equals(target.getSelectionModel().getSelectedText()));
    }
    boolean csharp(String lineText) {
      var target=selected();if(target==null)return false;
      if(!file("Graphs/Nodes.cs").equals(FileDocumentManager.getInstance().getFile(target.getDocument())))return false;
      var document=target.getDocument();int line=document.getLineNumber(target.getCaretModel().getOffset());
      return document.getText().substring(document.getLineStartOffset(line),document.getLineEndOffset(line)).contains(lineText);
    }
    void action(String id) {
      var action=ActionManager.getInstance().getAction(id);require(action!=null,"Missing registered product action "+id);
      var context=DataManager.getInstance().getDataContext(editor.getContentComponent());
      require(CommonDataKeys.PROJECT.getData(context)==project&&CommonDataKeys.EDITOR.getData(context)==editor,
          "Action lacks actual Editor/Project context: "+id);
      record("formal-action",Map.of("id",id,"path",FileDocumentManager.getInstance().getFile(editor.getDocument()).getPath(),
          "offset",editor.getCaretModel().getOffset(),"documentVersion",editor.getDocument().getModificationStamp()));
      action.actionPerformed(AnActionEvent.createFromAnAction(action,null,ActionPlaces.EDITOR_POPUP,context));
    }
    void navigation(String name,Place from,String action,Place to) {
      step(name,() -> {open(from);action(action);},() -> {
        if(!located(to,true))return false;
        record("actual-editor-target",Map.of("file",to.file(),"offset",selected().getCaretModel().getOffset(),"selection",to.token()));return true;
      });
    }
    void implementation(String name,Place from) {
      step(name,() -> {open(from);action(IMPL);},() -> {
        if(!csharp("class GraphWorker"))return false;record("actual-csharp-target","Graphs.GraphWorker");return true;
      });
    }
    Notification[] notifications() {
      return NotificationsManager.getNotificationsManager().getNotificationsOfType(Notification.class,project);
    }
    void baselineNotifications() { seen.clear();seen.addAll(Arrays.asList(notifications()));notificationEvidence=null; }
    boolean explanation(Predicate<String> expected) {
      for(var notification:notifications()) {
        if(seen.contains(notification))continue;
        String text=notification.getTitle()+" "+notification.getContent();
        if(!text.isBlank()&&expected.test(text)) { notificationEvidence=text;record("visible-explanation",text);return true; }
      }
      return false;
    }
    void rejected(String name,Place place,String id) {
      step(name,() -> {open(place);baselineNotifications();action(id);},() -> {
        require(located(place,false),"Rejected query navigated to a guessed target: "+name);
        return explanation(text -> text.contains("AnotherFSM")&&!text.matches("(?is).*(分析中|尚未完成|不可用|analyzing|not ready).*"));
      });
    }
    Content[] contents() {
      var window=ToolWindowManager.getInstance(project).getToolWindow("AnotherFSM");
      return window==null?new Content[0]:window.getContentManager().getContents();
    }
    List<Component> tree(Component component) {
      var result=new ArrayList<Component>();result.add(component);
      if(component instanceof Container container)for(var child:container.getComponents())result.addAll(tree(child));
      return result;
    }
    JList<?> list(Content page) {
      if(page==null)return null;
      for(var component:tree(page.getComponent()))if(component instanceof JList<?> list&&"AnotherFSM.references".equals(list.getName()))return list;
      return null;
    }
    List<String> rows(Content page) {
      var list=list(page);if(list==null)return List.of();var result=new ArrayList<String>();
      for(int i=0;i<list.getModel().getSize();i++)result.add(String.valueOf(list.getModel().getElementAt(i)));
      return result;
    }
    boolean fresh(Content page) { return page!=null&&page.getDisplayName().contains("AnotherFSM")&&!page.getDisplayName().startsWith("结果已过期"); }
    boolean expired(Content page) { return page!=null&&page.getDisplayName().startsWith("结果已过期"); }
    void references(String name,Place from,String id,int count,Predicate<List<String>> labels) {
      step(name,() -> {
        beforeContents.clear();beforeContents.addAll(Arrays.asList(contents()));open(from);action(id);
      },() -> {
        for(var page:contents())if(!beforeContents.contains(page)&&list(page)!=null&&fresh(page)) {
          var labelsNow=rows(page);
          if(labelsNow.size()!=count||!labels.test(labelsNow))continue;
          current=page;pages.add(page);record("actual-reference-page",Map.of("title",page.getDisplayName(),"rows",labelsNow));return true;
        }
        return false;
      });
    }
    boolean has(List<String> rows,String text) { return rows.stream().anyMatch(row -> row.contains(text)); }
    boolean lacks(List<String> rows,String text) { return rows.stream().noneMatch(row -> row.contains(text)); }
    String sourceLabel(String row) { int suffix=row.indexOf(" · 实例 ");return suffix<0?row:row.substring(0,suffix); }
    boolean sourceHas(List<String> rows,String text) { return rows.stream().anyMatch(row -> sourceLabel(row).contains(text)); }
    void pressEnter(JList<?> list) {
      require(ApplicationManager.getApplication().isDispatchThread(),"Swing Enter must run on EDT");
      require(list!=null&&list.isShowing()&&list.isEnabled()&&list.getSelectedIndex()>=0,"Enter target is not a visible enabled selected JList");
      // Component-local invocation only: JVM focus does NOT prove OS foreground ownership.
      // Never inject a Robot/global key, even when another application owns the foreground.
      var stroke=KeyStroke.getKeyStroke(KeyEvent.VK_ENTER,0);
      for(int condition:new int[]{JComponent.WHEN_FOCUSED,JComponent.WHEN_ANCESTOR_OF_FOCUSED_COMPONENT,JComponent.WHEN_IN_FOCUSED_WINDOW}) {
        var key=list.getInputMap(condition).get(stroke);
        var bound=key==null?null:list.getActionMap().get(key);
        if(bound==null||!bound.isEnabled())continue;
        record("public Swing Enter binding",Map.of("component",list.getClass().getName(),"binding",String.valueOf(key),"condition",condition,"physicalOsKey",false));
        bound.actionPerformed(new ActionEvent(list,ActionEvent.ACTION_PERFORMED,String.valueOf(key),System.currentTimeMillis(),0));return;
      }
      // Some popup lists use a public KeyListener instead of a Swing ActionMap binding.
      // Deliver only to listeners already attached to this verified component; no focus routing.
      var listeners=list.getKeyListeners();require(listeners.length>0,"No public Swing Enter binding or component-local KeyListener");
      record("component-local Enter KeyListener",Map.of("component",list.getClass().getName(),"listeners",listeners.length,"physicalOsKey",false));
      var pressed=new KeyEvent(list,KeyEvent.KEY_PRESSED,System.currentTimeMillis(),0,KeyEvent.VK_ENTER,KeyEvent.CHAR_UNDEFINED);
      for(var listener:listeners) {listener.keyPressed(pressed);if(pressed.isConsumed())break;}
      var released=new KeyEvent(list,KeyEvent.KEY_RELEASED,System.currentTimeMillis(),0,KeyEvent.VK_ENTER,KeyEvent.CHAR_UNDEFINED);
      for(var listener:listeners) {listener.keyReleased(released);if(released.isConsumed())break;}
    }
    void activate(Content page,String containing,boolean enter) {
      var window=ToolWindowManager.getInstance(project).getToolWindow("AnotherFSM");
      window.getContentManager().setSelectedContent(page);window.show(null);
      var list=list(page);require(list!=null,"No named references JList");
      int row=-1;for(int i=0;i<list.getModel().getSize();i++)if(sourceLabel(String.valueOf(list.getModel().getElementAt(i))).contains(containing)){row=i;break;}
      require(row>=0,"Missing UI row "+containing);list.setSelectedIndex(row);list.ensureIndexIsVisible(row);
      record("reference-row-activation",Map.of("row",String.valueOf(list.getModel().getElementAt(row)),"gesture",enter?"Enter":"doubleClick"));
      if(enter)pressEnter(list);
      else {
        var cell=list.getCellBounds(row,row);require(cell!=null,"Reference row has no actual cell bounds");
        list.dispatchEvent(new MouseEvent(list,MouseEvent.MOUSE_CLICKED,System.currentTimeMillis(),0,cell.x+3,cell.y+cell.height/2,2,false,MouseEvent.BUTTON1));
      }
    }
    void refresh(Content page) {
      var buttons=tree(page.getComponent()).stream().filter(component -> component instanceof JButton button&&"刷新".equals(button.getText())).toList();
      require(buttons.size()==1,"Expected exactly one public 刷新 JButton");((JButton)buttons.get(0)).doClick();record("refresh-click",page.getDisplayName());
    }
    void mutate(String relative,UnaryOperator<String> change) {
      // Model a real cross-file editor edit, so Rider owns/synchronizes its shared document.
      // Earlier assertions prove unopened discovery; a synthetic never-opened Document mutation
      // alone is not an edit through Rider's editor/document-binding seam.
      var editing=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,file(relative),0),true);
      require(editing!=null,"No actual editor for unsaved cross-file edit "+relative);
      var document=doc(relative);var next=change.apply(document.getText());require(!next.equals(document.getText()),"Fixture edit did not change "+relative);
      WriteCommandAction.runWriteCommandAction(project,() -> document.setText(next));
      require(FileDocumentManager.getInstance().isDocumentUnsaved(document),"Expected unsaved buffer edit "+relative);
      try { require(Files.readString(root.resolve(relative)).equals(originals.get(document)),"Fixture disk changed during unsaved test "+relative); }
      catch(java.io.IOException error) { throw new IllegalStateException(error); }
      record("unsaved-edit",relative);
    }
    void restore(String relative) {
      var document=doc(relative);String original=originals.get(document);
      if(!document.getText().equals(original))WriteCommandAction.runWriteCommandAction(project,() -> document.setText(original));
    }
    List<JList<?>> chooserLists() {
      var lists=new ArrayList<JList<?>>();
      for(var window:Window.getWindows())if(window.isShowing())for(var component:tree(window))
        if(component instanceof JList<?> list&&list.isShowing()&&!"AnotherFSM.references".equals(list.getName()))lists.add(list);
      return lists;
    }
    List<String> modelRows(JList<?> list) {
      var result=new ArrayList<String>();for(int i=0;i<list.getModel().getSize();i++)result.add(String.valueOf(list.getModel().getElementAt(i)));return result;
    }
    JList<?> publisherChooser;
    void chooserNavigation(String name,Place from,String id,int count,Predicate<List<String>> labels,int choice,Place... targets) {
      step(name,() -> {open(from);action(id);},() -> {
        for(var list:chooserLists()) {
          var values=modelRows(list);
          if(values.size()==count&&labels.test(values)) {
            publisherChooser=list;record("actual-location-chooser",values);return true;
          }
        }return false;
      });
      step(name+": selected real source location",() -> {publisherChooser.setSelectedIndex(choice);pressEnter(publisherChooser);},() -> Arrays.stream(targets).anyMatch(target -> located(target,true)));
    }
    void specify() {
      steps.add(new Step("pinned Rider and real RD readiness",() -> {
        require("262.10968.170".equals(ApplicationInfo.getInstance().getBuild().asStringWithoutProductCode()),"Wrong Rider build");
        require(Boolean.getBoolean("anotherfsm.acceptance"),"Only controlled acceptance sandbox is authorized");
        recordSentinelScope();
        require(project.getBasePath()!=null&&Path.of(project.getBasePath()).toAbsolutePath().normalize().equals(root.toAbsolutePath().normalize()),"Wrong fixture project root");
        for(String path:List.of(GRAPH,CHECKER,UNOPENED,"Graphs/Items/none.FSMScript","Graphs/Items/content.fsm","Graphs/Items/resource.FSMScript","SharedGraphs/linked.FSMScript"))
          FileEditorManager.getInstance(project).closeFile(file(path));
      },() -> {probeReadiness();return ready;},60_000));
      once("unopened project scripts remain closed",() -> {
        require(!FileEditorManager.getInstance(project).isFileOpen(file(UNOPENED)),"Solution reference fixture was already open");
        record("unopened-script",UNOPENED);
      });
      navigation("AdditionalFiles: Node is not factory key",at(GRAPH,"Launch -> Start to Left;","Start"),DECL,at(GRAPH,"def Start(GraphKey)","Start"));
      navigation("import file target uses Checker.fsm, not alias or decoy",at(GRAPH,"import Checker as Alias;","Checker"),DECL,at(CHECKER,"module Checker {","Checker"));
      navigation("alias use defaults to alias declaration",at(GRAPH,"use Alias as Left;","Alias"),DECL,at(GRAPH,"import Checker as Alias;","Alias"));
      navigation("alias has separate module-definition operation",at(GRAPH,"use Alias as Left;","Alias"),"AnotherFSM.GoToModuleDefinition",at(CHECKER,"module Checker {","Checker"));
      navigation("relative transitive import",at(CHECKER,"import Leaf;","Leaf"),DECL,at("Graphs/Flow/Leaf.fsm","module Leaf {","Leaf"));
      navigation("qualified instance prefix is use declaration",at(GRAPH,"Left.Done -> Right;","Left"),DECL,at(GRAPH,"use Alias as Left;","Left"));
      navigation("Input suffix defaults to interface",at(GRAPH,"LaunchEntry -> Start to Left.Entry;","Entry"),DECL,at(CHECKER,"input Entry;","Entry"));
      navigation("Input internal definition shares Node identity",at(CHECKER,"input Entry;","Entry"),"AnotherFSM.GoToInternal",at(CHECKER,"def Entry(GraphKey)","Entry"));
      navigation("Output suffix defaults to interface",at(GRAPH,"Left.Done -> Right;","Done"),DECL,at(CHECKER,"output Done;","Done"));
      navigation("single Input omission defaults to instance",at(GRAPH,"Launch -> Start to Left;","Left"),DECL,at(GRAPH,"use Alias as Left;","Left"));
      navigation("single Input explicit entry operation",at(GRAPH,"Launch -> Start to Left;","Left"),"AnotherFSM.GoToEntry",at(CHECKER,"input Entry;","Entry"));
      navigation("multiple Input First",at(GRAPH,"LaunchFirst -> Start to Many.First;","First"),DECL,at(GRAPH,"input First, Second;","First"));
      navigation("multiple Input Second",at(GRAPH,"LaunchSecond -> Start to Many.Second;","Second"),DECL,at(GRAPH,"input First, Second;","Second"));
      navigation("module A as B registers only B",at(GRAPH,"use Renamed as Registered;","Renamed"),DECL,at(GRAPH,"module Original as Renamed {","Renamed"));
      navigation("explicit Event distinct from same-named Node",at(GRAPH,"Signal -> Signal to End;","Signal"),DECL,at(GRAPH,"def Signal event;","Signal"));
      // Select the second Signal explicitly: the anchor starts at that token, unlike the Event part.
      navigation("same-named Node remains Node",at(GRAPH,"Signal to End;","Signal"),DECL,at(GRAPH,"def Signal(GraphKey)","Signal"));
      navigation("ordinary explicit Event",at(GRAPH,"Explicit -> Start to End;","Explicit"),DECL,at(GRAPH,"def Explicit event;","Explicit"));
      navigation("Namespace prefix resolves local Node",at(GRAPH,"Moved -> Local to Exit;","Local"),DECL,at(GRAPH,"def Local(GraphKey)","Local"));
      navigation("dotted ordinary name is not Module Instance",at(GRAPH,"LaunchDotted -> Start to Plain.Dotted;","Plain.Dotted"),DECL,at(GRAPH,"def Plain.Dotted(GraphKey)","Plain.Dotted"));
      navigation("Namespace-qualified Node suffix",at(GRAPH,"LaunchLocal -> Start to Zone.Local;","Local"),DECL,at(GRAPH,"def Local(GraphKey)","Local"));
      chooserNavigation("merged Namespace declarations are multiple real source positions",at(GRAPH,"namespace Zone {\n    def Local","Zone"),DECL,2,
          rows -> rows.stream().allMatch(row -> row.contains("Zone")),0,at(GRAPH,"namespace Zone {\n    def Local","Zone"),at(GRAPH,"namespace Zone {\n    def Tail","Zone"));
      for(var item:List.of(new String[]{"None","Graphs/Items/none.FSMScript","NoneEntry","NoneEvent"},
          new String[]{"Content","Graphs/Items/content.fsm","ContentEntry","ContentEvent"},
          new String[]{"EmbeddedResource","Graphs/Items/resource.FSMScript","ResourceEntry","ResourceEvent"})) {
        navigation(item[0]+": real project-item declaration",at(item[1],item[3]+" -> "+item[2]+" to",item[2]),DECL,at(item[1],"def "+item[2]+"(GraphKey)",item[2]));
        implementation(item[0]+": host membership and GraphKey binding",at(item[1],"def "+item[2]+"(GraphKey)","GraphKey"));
      }
      navigation("linked file uses physical directory not Link display",at("SharedGraphs/linked.FSMScript","import LinkedChecker as LinkedAlias;","LinkedChecker"),DECL,
          at("SharedGraphs/LinkedChecker.fsm","module LinkedChecker {","LinkedChecker"));
      implementation("linked file retains owning project binding",at("SharedGraphs/linked.FSMScript","def LinkedEnd(GraphKey)","GraphKey"));
      once("change Content factory to another valid key without saving",() -> mutate("Graphs/Items/content.fsm",text -> text.replace("GraphKey","GraphBareKey")));
      step("unsaved Content key navigates using real project buffer, not disk path document",() -> {
        open(at("Graphs/Items/content.fsm","def ContentEntry(GraphBareKey)","GraphBareKey"));action(IMPL);
      },() -> csharp("class BareWorker"));
      once("restore unsaved Content factory",() -> restore("Graphs/Items/content.fsm"));
      implementation("Module Input C# implementation",at(CHECKER,"input Entry;","Entry"));
      implementation("qualified Input C# implementation",at(GRAPH,"LaunchEntry -> Start to Left.Entry;","Entry"));
      rejected("Output has no fabricated C# type",at(GRAPH,"Left.Done -> Right;","Done"),IMPL);
      rejected("Module Instance is not an executable Node",at(GRAPH,"Left.Done -> Right;","Left"),IMPL);
      rejected("Event has no fabricated C# type",at(GRAPH,"Explicit -> Start to End;","Explicit"),IMPL);
      rejected("Namespace has no C# namespace binding",at(GRAPH,"namespace Zone {\n    def Local","Zone"),IMPL);
      for(var item:List.of(new String[]{"error","3"},new String[]{"break","4"},new String[]{"cancel","5"})) {
        var place=at(GRAPH,item[0]+"->",item[0]);
        step("Branch "+item[0]+" is DSL index "+item[1]+", not C# enum",() -> {open(place);baselineNotifications();action("AnotherFSM.BranchMetadata");},() -> {
          require(located(place,false),"Branch without metadata navigated to C# enum");
          return explanation(text -> text.matches("(?s).*(?<![0-9-])"+item[1]+"(?![0-9]).*"));
        });
      }
      step("Branch with explicit metadata locates its real attribute, not a return or enum",() -> {
        open(at(GRAPH,"success->Launch;","success"));action("AnotherFSM.BranchMetadata");
      },() -> csharp("[FSMNode(\"GraphKey\""));
      references("Module references include both aliases and unopened dependent script",at(CHECKER,"module Checker {","Checker"),REFS,5,
          rows -> has(rows,"graph.fsm")&&has(rows,"unopened.FSMScript"));
      references("import target references share actual Module identity",at(GRAPH,"import Checker as Alias;","Checker"),REFS,5,
          rows -> has(rows,"graph.fsm")&&has(rows,"unopened.FSMScript"));
      references("Namespace references are real prefix uses, not declarations",at(GRAPH,"namespace Zone {\n    def Local","Zone"),REFS,3,
          rows -> rows.stream().allMatch(row -> row.contains("graph.fsm")));
      references("Branch query groups error and numeric 3 only for this Node",at(GRAPH,"error->Fault;","error"),REFS,2,
          rows -> rows.stream().allMatch(row -> row.contains("graph.fsm")));
      references("instance query stays separate from Right",at(GRAPH,"use Alias as Left;","Left"),REFS,3,
          rows -> has(rows,"Left")&&lacks(rows,"Right")&&lacks(rows,"Unopened"));
      references("whole instance can switch to Module template references",at(GRAPH,"use Alias as Left;","Left"),TEMPLATE,5,
          rows -> sourceHas(rows,"graph.fsm")&&sourceHas(rows,"unopened.FSMScript"));
      references("alias references exclude module/other alias declarations",at(GRAPH,"import Checker as Alias;","Alias"),REFS,2,
          rows -> has(rows,"graph.fsm")&&lacks(rows,"unopened.FSMScript"));
      references("explicit Event references exclude Node with same spelling",at(GRAPH,"def Signal event;","Signal"),REFS,1,rows -> has(rows,"graph.fsm"));
      step("reference Enter uses actual Event span",() -> activate(current,"graph.fsm",true),() -> located(at(GRAPH,"Signal -> Signal to End;","Signal"),true));
      references("Node references exclude Event with same spelling",at(GRAPH,"def Signal(GraphKey)","Signal"),REFS,1,rows -> has(rows,"graph.fsm"));
      step("reference doubleClick uses actual Node span",() -> activate(current,"graph.fsm",false),() -> located(at(GRAPH,"Signal to End;","Signal"),true));
      chooserNavigation("implicit Event declaration action offers introduction/use locations",at("Graphs/Flow/implicit.fsm","Pulse -> Source to Target;","Pulse"),DECL,3,
          rows -> rows.stream().allMatch(row -> row.contains("Pulse")),0,at("Graphs/Flow/implicit.fsm","1->Pulse;","Pulse"),at("Graphs/Flow/implicit.fsm","2->Pulse;","Pulse"),at("Graphs/Flow/implicit.fsm","Pulse -> Source to Target;","Pulse"));
      references("implicit Event has all introduction/use locations, no invented declaration",at("Graphs/Flow/implicit.fsm","Pulse -> Source to Target;","Pulse"),REFS,3,
          rows -> rows.stream().allMatch(row -> row.contains("implicit.fsm")));
      references("Left Output query includes internal uses but not Right/Unopened",at(GRAPH,"Left.Done -> Right;","Done"),REFS,5,
          rows -> has(rows,"Left")&&lacks(rows,"Right")&&lacks(rows,"Unopened"));
      references("template switch aggregates three instances and unopened file, deduped real positions",at(GRAPH,"Left.Done -> Right;","Done"),TEMPLATE,7,
          rows -> has(rows,"Left")&&has(rows,"Right")&&has(rows,"Unopened")&&has(rows,"unopened.FSMScript"));
      once("solution references did not require opening dependent script",() -> require(!FileEditorManager.getInstance(project).isFileOpen(file(UNOPENED)),"Find references opened an unopened dependent script"));
      references("template declaration has same reference scope",at(CHECKER,"output Done;","Done"),REFS,7,
          rows -> has(rows,"Left")&&has(rows,"Right")&&has(rows,"Unopened"));
      references("instance Input keeps Left context",at(GRAPH,"LaunchEntry -> Start to Left.Entry;","Entry"),REFS,4,
          rows -> has(rows,"Left")&&lacks(rows,"Right")&&lacks(rows,"Unopened"));
      references("Input template switch merges real internal/external Node references",at(GRAPH,"LaunchEntry -> Start to Left.Entry;","Entry"),TEMPLATE,5,
          rows -> has(rows,"Left")&&has(rows,"Right")&&has(rows,"Unopened"));
      references("internal Node and Input declarations query the same identity",at(CHECKER,"def Entry(GraphKey)","Entry"),REFS,5,
          rows -> has(rows,"Left")&&has(rows,"Right")&&has(rows,"Unopened"));
      // Multi-target assertion reads the real chooser, not a JSON response. PublisherA has three
      // surviving Done branches; duplicate branches and overwritten Discarded are not extra nodes.
      step("Output internal publishers: two real Nodes, same-Node branches deduplicated",() -> {
        open(at(CHECKER,"output Done;","Done"));action("AnotherFSM.GoToInternal");
      },() -> {
        for(var list:chooserLists()) {
          var labels=modelRows(list);
          if(labels.size()==2&&has(labels,"PublisherA")&&has(labels,"PublisherB")) {
            publisherChooser=list;record("actual-publisher-chooser",labels);capture("dsl-publishers.png");return true;
          }
        }return false;
      });
      step("chooser selection navigates to real template source",() -> {
        var labels=modelRows(publisherChooser);publisherChooser.setSelectedIndex(labels.get(0).contains("PublisherA")?0:1);pressEnter(publisherChooser);
      },() -> located(at(CHECKER,"def PublisherA(GraphKey)","PublisherA"),true));
      String chain="Graphs/Flow/edges/alias-chain.fsm",chainModule="Graphs/Flow/edges/Chain.fsm";
      navigation("chained alias keeps its own default declaration",at(chain,"use SecondAlias as Chained;","SecondAlias"),DECL,
          at(chain,"import FirstAlias as SecondAlias;","SecondAlias"));
      navigation("chained alias Module definition reaches real template",at(chain,"use SecondAlias as Chained;","SecondAlias"),"AnotherFSM.GoToModuleDefinition",
          at(chainModule,"module Chain {","Chain"));
      references("Module query includes transitive aliases",at(chainModule,"module Chain {","Chain"),REFS,3,rows -> sourceHas(rows,"alias-chain.fsm"));
      references("first alias query stays local to its own import use",at(chain,"import Chain as FirstAlias;","FirstAlias"),REFS,1,rows -> sourceHas(rows,"alias-chain.fsm"));
      String damaged="Graphs/Flow/negative/damaged-inputs.fsm";
      rejected("damaged multiple Inputs do not authorize implicit target",at(damaged,"Go -> Start to Bad;","Bad"),DECL);
      rejected("damaged multiple Inputs have no unique entry operation",at(damaged,"use DamagedMany as Bad;","Bad"),"AnotherFSM.GoToEntry");
      reverseCsharp();
      negativeCases();
      untitledCases();
      once("leave unrelated incomplete syntax unsaved",() -> mutate(GRAPH,text -> text+"\ndef Unfinished("));
      navigation("reliable Module interface survives unrelated incomplete tail",at(GRAPH,"Left.Done -> Right;","Done"),DECL,at(CHECKER,"output Done;","Done"));
      once("restore incomplete tail",() -> restore(GRAPH));
      staleCases();
      once("final screenshot",() -> capture("dsl-host-acceptance.png"));
    }

    void reverseCsharp() {
      step("native C# Find Usages finds real keys across all project item kinds",() -> {
        open(at("Graphs/Nodes.cs","class GraphWorker :","GraphWorker"));action("FindUsages");
      },() -> {
        var window=ToolWindowManager.getInstance(project).getToolWindow("Find");
        if(window==null||window.getContentManager().getSelectedContent()==null
            ||!window.getContentManager().getSelectedContent().getDisplayName().contains("GraphWorker"))return false;
        var content=window.getContentManager().getSelectedContent();
        var view=UsageView.USAGE_VIEW_KEY.getData(DataManager.getInstance().getDataContext(content.getComponent()));
        if(!(view instanceof UsageViewImpl nativeView))return false;
        var sessions=Collections.newSetFromMap(new IdentityHashMap<RdFindUsagesSession,Boolean>());
        for(var target:nativeView.getTargets())if(target instanceof FrontendUsageTarget nativeTarget)sessions.add(nativeTarget.getSession());
        if(sessions.size()!=1||!Boolean.TRUE.equals(sessions.iterator().next().isSearchComplete().getValueOrNull()))return false;
        try {
          var path=output.resolve("native-observation.json");if(!Files.isRegularFile(path))return false;
          var sample=JsonParser.parseString(Files.readString(path)).getAsJsonObject();
          for(var value:sample.getAsJsonArray("descriptors")) {
            var descriptor=value.getAsJsonObject();
            if(!descriptor.get("title").getAsString().contains("GraphWorker")||descriptor.get("updating").getAsBoolean())continue;
            var found=new HashSet<String>();var positions=new HashSet<String>();var evidence=new JsonArray();
            for(var item:descriptor.getAsJsonArray("items")) {
              var row=item.getAsJsonObject();
              if(!row.has("runtimeType")||row.get("runtimeType").isJsonNull()||!row.get("runtimeType").getAsString().equals("AnotherFSM.Rider.FsmOccurrence"))continue;
              require(row.get("valid").getAsBoolean(),"Native GraphWorker occurrence was already invalid");
              require("GraphKey".equals(row.get("currentSlice").getAsString()),"C# reverse search fabricated a Node/Transition usage instead of factory key");
              String file=row.get("file").getAsString();
              require(positions.add(file+":"+row.get("start")+":"+row.get("length")),"Native C# reverse search duplicated a template source position");
              require(!file.contains("/Loose/")&&!file.contains("/Decoys/")&&!file.contains("/DisplayOnly/"),"Native search assigned an unowned script to Graphs");
              found.add(file);evidence.add(row);
            }
            var expected=List.of(GRAPH,CHECKER,UNOPENED,"Graphs/Flow/Leaf.fsm","Graphs/Items/none.FSMScript","Graphs/Items/content.fsm","Graphs/Items/resource.FSMScript","SharedGraphs/linked.FSMScript","SharedGraphs/LinkedChecker.fsm");
            if(expected.stream().allMatch(relative -> found.stream().anyMatch(file -> file.endsWith(relative)))) {
              record("actual-native-GraphWorker-keys",evidence);record("native-query-complete",true);
              for(var component:tree(content.getComponent()))if(component instanceof JTree resultTree)
                for(int row=0;row<resultTree.getRowCount()&&row<512;row++)resultTree.expandRow(row);
              capture("dsl-native-csharp-reverse.png");return true;
            }
          }
          return false;
        } catch(java.io.IOException failure) {throw new IllegalStateException(failure);}
      });
    }
    void untitledCases() {
      step("no-directory unsaved editor retains reliable local declaration",() -> {
        untitled=new LightVirtualFile("issue33-untitled.fsm","import Checker;\ndef Local(GraphKey) { }\ndef End(GraphKey) { }\nMove -> Local to End;\n");
        editor=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,untitled,0),true);
        require(editor!=null&&!untitled.isInLocalFileSystem(),"Expected real non-physical unsaved editor");
        editor.getCaretModel().moveToOffset(editor.getDocument().getText().indexOf("Local to End"));action(DECL);
      },() -> selected()==editor&&editor.getCaretModel().getOffset()==editor.getDocument().getText().indexOf("def Local(")+4&&"Local".equals(editor.getSelectionModel().getSelectedText()));
      step("no-directory relative import explains absent context without guessing",() -> {
        editor.getSelectionModel().removeSelection();editor.getCaretModel().moveToOffset(7);baselineNotifications();action(DECL);
      },() -> {
        require(selected()==editor&&editor.getCaretModel().getOffset()==7,"No-directory import navigated to guessed project file");
        return explanation(text -> text.matches("(?is).*(目录|上下文|未配置|宿主|directory|context|host|not found|未找到).*"));
      });
      step("no-host factory binding stays unresolved",() -> {
        editor.getCaretModel().moveToOffset(editor.getDocument().getText().indexOf("GraphKey"));baselineNotifications();action(IMPL);
      },() -> {
        require(selected()==editor&&editor.getCaretModel().getOffset()==editor.getDocument().getText().indexOf("GraphKey"),"Untitled key borrowed a project host");
        return explanation(text -> text.matches("(?is).*(上下文|未配置|宿主|context|host).*"));
      });
      once("close no-directory editor without saving",() -> FileEditorManager.getInstance(project).closeFile(untitled));
    }
    void negativeCases() {
      rejected("missing import does not guess same-named solution file",at("Graphs/Flow/negative/missing.fsm","import Checker as MissingAlias;","Checker"),DECL);
      rejected("import M still requires M.fsm, not discovered M.FSMScript",at("Graphs/Flow/negative/missing.fsm","import ExtensionOnly;","ExtensionOnly"),DECL);
      for(String relative:List.of("missing.fsm","cyclic.fsm","broken-import.fsm")) {
        String path="Graphs/Flow/negative/"+relative;
        navigation("reliable local Node survives "+relative,at(path,"SafeEvent -> Safe to End;","Safe"),DECL,at(path,"def Safe(GraphKey)","Safe"));
      }
      rejected("cyclic import reports why no target is safe",at("Graphs/Flow/negative/cyclic.fsm","use CycleA as Loop;","CycleA"),"AnotherFSM.GoToModuleDefinition");
      rejected("broken imported interface is not a certain target",at("Graphs/Flow/negative/broken-import.fsm","use Broken as Damaged;","Broken"),"AnotherFSM.GoToEntry");
      String invalid="Graphs/Flow/negative/invalid.fsm";
      rejected("use requires an already available Module",at(invalid,"use Later as Premature;","Later"),DECL);
      rejected("module original name is not also registered",at(invalid,"use Original as WrongRegistration;","Original"),DECL);
      rejected("multiple Inputs require explicit choice",at(invalid,"Launch -> Start to Many;","Many"),"AnotherFSM.GoToEntry");
      rejected("private Node cannot be selected as public Input",at(invalid,"Launch -> Start to Many.Private;","Private"),DECL);
      rejected("instance cannot be Transition source Node",at(invalid,"Launch -> Many to Start;","Many"),IMPL);
      rejected("overwritten Branch is not a real Output publisher",at("Graphs/Flow/negative/overwrite.fsm","output Done;","Done"),"AnotherFSM.GoToInternal");
      rejected("legacy module def is not a Node implementation",at("Graphs/Flow/negative/legacy.fsm","def Old(Legacy)","Legacy"),IMPL);
      navigation("unowned loose script retains local assistance",at("Graphs/Loose/graph.fsm","LocalEvent -> Local to End;","Local"),DECL,at("Graphs/Loose/graph.fsm","def Local(GraphKey)","Local"));
      rejected("directory proximity does not grant project membership",at("Graphs/Loose/graph.fsm","def Local(GraphKey)","GraphKey"),IMPL);
      rejected("loose relative import does not guess project module",at("Graphs/Loose/graph.fsm","import Checker;","Checker"),DECL);
    }
    void staleCases() {
      references("stale baseline page",at(CHECKER,"output Done;","Done"),REFS,7,rows -> has(rows,"unopened.FSMScript"));
      once("retain first query identity",() -> {oldPage=current;oldRows=rows(current);});
      references("same-title second query gets independent Content",at(CHECKER,"output Done;","Done"),REFS,7,rows -> has(rows,"unopened.FSMScript"));
      once("retain second query identity",() -> {
        otherPage=current;otherRows=rows(current);require(oldPage!=otherPage,"Two queries reused one Content");
        require(oldPage.getDisplayName().equals(otherPage.getDisplayName()),"Expected same-title independent queries");
        stationary=at(GRAPH,"def Start(GraphKey)","Start");open(stationary);
        mutate(UNOPENED,text -> text.replace("Unopened.Done","Unopened.Gone"));
      });
      step("dependent unsaved change actively expires both pages without reordering",() -> {},() -> {
        if(!expired(oldPage)||!expired(otherPage))return false;
        require(rows(oldPage).equals(oldRows)&&rows(otherPage).equals(otherRows),"Expired result list silently reordered");
        capture("dsl-expired-references.png");record("expired-content-titles",List.of(oldPage.getDisplayName(),otherPage.getDisplayName()));return true;
      });
      for(boolean enter:List.of(false,true)) {
        step("stale row "+(enter?"Enter":"doubleClick")+" revalidates and rejects navigation",() -> {
          open(stationary);baselineNotifications();activate(oldPage,"unopened.FSMScript",enter);
        },() -> {
          require(located(stationary,false),"Stale row navigated to old source location");
          require(rows(oldPage).equals(oldRows),"Stale activation reordered results");
          return explanation(text -> text.matches("(?is).*(过期|刷新|stale|refresh).*"));
        });
      }
      step("refresh applies current unsaved dependency only to selected query",() -> refresh(oldPage),() -> {
        if(!fresh(oldPage)||rows(oldPage).size()!=6)return false;
        require(!sourceHas(rows(oldPage),"unopened.FSMScript"),"Refresh kept removed dependency source position (instance context labels are not source positions)");
        require(expired(otherPage)&&rows(otherPage).equals(otherRows),"Refreshing one query mutated the other Content");
        record("refreshed-public-model",rows(oldPage));capture("dsl-refreshed-references.png");return true;
      });
      once("template edit invalidates dependency results",() -> mutate(CHECKER,text -> text.replace("Done","Finished")));
      step("template change actively expires refreshed results",() -> {},() -> expired(oldPage));
      once("restore template and dependency",() -> {restore(CHECKER);restore(UNOPENED);});
      step("refresh after restore finds all real references again",() -> refresh(oldPage),() -> fresh(oldPage)&&rows(oldPage).size()==7&&sourceHas(rows(oldPage),"unopened.FSMScript"));
      once("import edit invalidates template relation",() -> mutate(GRAPH,text -> text.replace("import Checker as Alias;","import Missing as Alias;")));
      step("import change actively expires open reference query",() -> {},() -> expired(oldPage));
      once("restore import",() -> restore(GRAPH));
      step("refresh after import restoration",() -> refresh(oldPage),() -> fresh(oldPage)&&rows(oldPage).size()==7);
      // A fresh result is still navigated through the public list; no RD-return shortcut.
      step("fresh result navigates to actual external source",() -> activate(oldPage,"unopened.FSMScript",true),
          () -> located(at(UNOPENED,"Unopened.Done -> Finish;","Done"),true));
    }

    void probeReadiness() {
      if(ready)return;
      if(inFlight) {
        if(since(queryStart)>5_000) {generation++;inFlight=false;if(query!=null)Disposer.dispose(query);record("rd-readiness-timeout",true);}return;
      }
      var document=doc(GRAPH);var request=new JsonObject();
      request.addProperty("action","resolve");request.addProperty("path",file(GRAPH).getPath());request.addProperty("text",document.getText());
      request.addProperty("documentVersion",document.getModificationStamp());request.addProperty("offset",offset(document,at(GRAPH,"def Start(GraphKey)","GraphKey")));
      request.addProperty("requestId",UUID.randomUUID().toString());request.addProperty("implementation",true);
      long own=++generation;inFlight=true;queryStart=System.nanoTime();record("actual-rd-request",request);
      query=BackendConnection.get(project).query(request.toString(),raw -> {
        if(done||own!=generation)return;
        try {
          inFlight=false;record("actual-rd-reply",raw);var reply=JsonParser.parseString(raw).getAsJsonObject();
          if(!reply.has("status")||!"type".equals(reply.get("status").getAsString()))return;
          require(request.get("requestId").equals(reply.get("requestId")),"RD requestId mismatch");
          require(request.get("documentVersion").equals(reply.get("documentVersion")),"RD documentVersion mismatch");
          require(reply.has("session")&&!reply.get("session").getAsString().isBlank()&&reply.get("contextVersion").getAsLong()>=0,"Missing real RD session/contextVersion");
          ready=true;
        } catch(Throwable failure) {finish(false,failure.toString());}
      },failure -> {if(!done&&own==generation){inFlight=false;record("rd-not-ready",failure.toString());}});
    }
    void tick() {
      if(done)return;require(!project.isDisposed(),"Project disposed during DSL journey");checkSentinels();
      if(index==steps.size()){finish(true,null);return;}
      require(since(begun)<600_000,"DSL journey exceeded 600s total deadline");
      var step=steps.get(index);
      if(!step.started) {step.started=true;step.begun=System.nanoTime();step.status="running";record("begin",step.name);report("running",null);step.enter.run();return;}
      if(step.verify.getAsBoolean()) {
        step.status="passed";record("passed",step.name);index++;report("running",null);
      } else require(since(step.begun)<step.limit,"Deadline: "+step.name+"; selected="+selectedDescription()+"; UI rows="+rows(current));
    }
    String selectedDescription() {
      var editor=selected();if(editor==null)return "none";var file=FileDocumentManager.getInstance().getFile(editor.getDocument());
      return (file==null?"untitled":file.getPath())+":"+editor.getCaretModel().getOffset()+":"+editor.getSelectionModel().getSelectedText();
    }
    void capture(String name) {
      try {
        for(var window:Window.getWindows())if(window.isShowing()&&window instanceof Frame&&window.getWidth()>500) {
          Files.createDirectories(output);ImageIO.write(new Robot().createScreenCapture(window.getBounds()),"png",output.resolve(name).toFile());record("screenshot",name);return;
        }
        throw new IllegalStateException("No visible Rider frame for screenshot");
      } catch(Exception failure) {throw new IllegalStateException("Cannot preserve Rider screenshot",failure);}
    }
    void report(String status,String error) {
      try {
        Files.createDirectories(output);var report=new JsonObject();report.addProperty("status",status);report.addProperty("step",index);
        report.addProperty("elapsedMs",since(begun));report.addProperty("error",error);report.addProperty("seam","real Rider actions/editor/Swing UI; RD readiness evidence only");
        var coverage=new JsonArray();for(var step:steps) {var item=new JsonObject();item.addProperty("name",step.name);item.addProperty("status",step.status);coverage.add(item);}
        report.add("coverage",coverage);report.add("observations",gson.toJsonTree(events));
        // Deliberately not claimed by this journey: these require the core/SDK regression suites.
        report.add("notClaimed",gson.toJsonTree(List.of("runtime/generator regression execution", "deterministic delayed session/contextVersion packet ordering", "cross-project/TFM/external/JSON (#34)", "completion/documentation (#35)", "performance (#36)")));
        Files.writeString(output.resolve("dsl-host-acceptance.json"),gson.toJson(report));
      } catch(Exception failure) {failure.printStackTrace();}
    }
    void finish(boolean passed,String error) {
      if(done)return;done=true;if(timer!=null)timer.cancel(false);generation++;if(query!=null)Disposer.dispose(query);
      if(!passed) {if(index<steps.size())steps.get(index).status="failed";try {capture("dsl-host-failure.png");}catch(Throwable failure){record("screenshot-failure",failure.toString());}}
      try {
        for(var entry:originals.entrySet())if(!project.isDisposed()) {
          if(!entry.getKey().getText().equals(entry.getValue()))WriteCommandAction.runWriteCommandAction(project,() -> entry.getKey().setText(entry.getValue()));
          FileDocumentManager.getInstance().saveDocument(entry.getKey());
        }
      } catch(Throwable failure) {passed=false;error=String.valueOf(error)+"; cleanup: "+failure;}
      if(untitled!=null&&!project.isDisposed())FileEditorManager.getInstance(project).closeFile(untitled);
      try {checkSentinels();record("instrumented-business-markers-not-created",sentinelPaths().stream().map(Path::toString).toList());}
      catch(Throwable failure) {passed=false;error=String.valueOf(error)+"; "+failure;}
      report(passed?"passed":"failed",error);
      if(passed)onSuccess.run();
    }
  }
}
