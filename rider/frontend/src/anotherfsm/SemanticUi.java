package anotherfsm;

import com.google.gson.*;
import com.intellij.notification.*;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.components.Service;
import com.intellij.openapi.diagnostic.Logger;
import com.intellij.openapi.editor.*;
import com.intellij.openapi.editor.event.*;
import com.intellij.openapi.fileEditor.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.ui.popup.*;
import com.intellij.openapi.util.Disposer;
import com.intellij.openapi.vfs.LocalFileSystem;
import com.intellij.openapi.wm.*;
import com.intellij.ui.components.JBList;
import com.intellij.ui.content.*;
import com.intellij.util.concurrency.AppExecutorUtil;
import java.awt.BorderLayout;
import java.awt.event.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Consumer;
import javax.swing.*;

/** Versioned asynchronous presentation only. No duplicated DSL binding, scope or import rules. */
@Service(Service.Level.PROJECT)
public final class SemanticUi implements Disposable {
  private static final Logger LOG=Logger.getInstance(SemanticUi.class);
  private final Project project;
  private final Set<Ticket> requests=Collections.newSetFromMap(new IdentityHashMap<>());
  private final Set<ResultPage> pages=Collections.newSetFromMap(new IdentityHashMap<>());
  private final Map<Document,Ticket> navigation=new IdentityHashMap<>();
  private String session;
  private long context=-1;
  private boolean disposed;

  private static final class Origin implements Disposable {
    final Document document;final String path,action,operation;final boolean implementation;final RangeMarker anchor;
    Origin(Editor editor,String action,String operation,boolean implementation) {
      document=editor.getDocument();var file=FileDocumentManager.getInstance().getFile(document);
      path=file==null?"untitled":file.getPath();this.action=action;this.operation=operation;this.implementation=implementation;
      anchor=document.createRangeMarker(editor.getCaretModel().getOffset(),editor.getCaretModel().getOffset());
    }
    public void dispose() {anchor.dispose();}
  }
  private static final class Ticket {
    final Origin origin;final String text,id=UUID.randomUUID().toString();final long version;final int offset;
    final JsonObject envelope=new JsonObject();Disposable request;ScheduledFuture<?> timeout;boolean done;
    JBPopup popup;String resultSession;long resultEpoch=-1;
    Ticket(Origin origin) {
      this.origin=origin;text=origin.document.getText();version=origin.document.getModificationStamp();offset=origin.anchor.getStartOffset();
      envelope.addProperty("action",origin.action);envelope.addProperty("operation",origin.operation);
      envelope.addProperty("implementation",origin.implementation);envelope.addProperty("path",origin.path);
      envelope.addProperty("text",text);envelope.addProperty("offset",offset);envelope.addProperty("documentVersion",version);
      envelope.addProperty("requestId",id);
    }
    boolean sameDocument() {return origin.anchor.isValid()&&origin.document.getModificationStamp()==version&&origin.document.getText().equals(text);}
    void cancel() {done=true;if(request!=null)Disposer.dispose(request);if(timeout!=null)timeout.cancel(false);if(popup!=null)popup.cancel();}
  }
  private static final class Location {
    final JsonObject value;
    Location(JsonObject value) {this.value=value.deepCopy();}
    public String toString() {
      var text=value.get("text").getAsString();int offset=value.getAsJsonObject("span").get("start").getAsInt();
      long line=1+text.substring(0,Math.min(offset,text.length())).chars().filter(c -> c=='\n').count();
      var instances=new ArrayList<String>();for(var instance:value.getAsJsonArray("instances"))instances.add(instance.getAsString());
      var hosts=new ArrayList<String>();if(value.has("hosts")&&!value.get("hosts").isJsonNull())
        for(var host:value.getAsJsonArray("hosts"))hosts.add(host.getAsString());
      return value.get("kind").getAsString()+" "+value.get("label").getAsString()+" — "+value.get("documentId").getAsString()+":"+line
          +(instances.isEmpty()?"":" · 实例 "+String.join(", ",instances))+(hosts.isEmpty()?"":" · 宿主 "+String.join(", ",hosts));
    }
  }

  public SemanticUi(Project project) {
    this.project=project;BackendConnection.get(project).onInvalidated(this::invalidated);
    EditorFactory.getInstance().getEventMulticaster().addDocumentListener(new DocumentListener() {
      public void documentChanged(DocumentEvent event) {
        Runnable invalidate=() -> {
          // A dependency buffer can change while the originating buffer is unchanged. Cancel local
          // in-flight operations immediately; do not wait for a later backend invalidation packet.
          for(var ticket:new ArrayList<>(requests))finish(ticket);
          // Conservative invalidation is deliberate: a changed import/template may affect any open query.
          for(var page:pages)page.expire();
        };
        if(ApplicationManager.getApplication().isDispatchThread())invalidate.run();else ApplicationManager.getApplication().invokeLater(invalidate);
      }
    },this);
    EditorFactory.getInstance().addEditorFactoryListener(new EditorFactoryListener() {
      public void editorReleased(EditorFactoryEvent event) {var active=navigation.remove(event.getEditor().getDocument());if(active!=null)finish(active);}
    },this);
  }
  private boolean live() {return !disposed&&!project.isDisposed();}
  private void inform(String reason) {
    if(live())NotificationGroupManager.getInstance().getNotificationGroup("AnotherFSM")
        .createNotification("AnotherFSM："+reason,NotificationType.INFORMATION).notify(project);
  }
  private void invalidated(String raw) {
    if(!live())return;
    try {
      var event=JsonParser.parseString(raw).getAsJsonObject();
      if(event.has("transportInvalidated"))throw new IllegalStateException("Transport changed");
      var next=event.get("session").getAsString();long epoch=event.get("contextVersion").getAsLong();
      if(session!=null&&session.equals(next)&&epoch<context)return;
      boolean replaced=session!=null&&!session.equals(next);
      session=next;context=replaced?epoch:Math.max(context,epoch);
      for(var ticket:new ArrayList<>(requests))
        if(replaced||(ticket.resultEpoch>=0&&(!Objects.equals(ticket.resultSession,session)||ticket.resultEpoch<context)))finish(ticket);
      for(var page:pages)if(!Objects.equals(page.session,session)||page.epoch<context)page.expire();
    } catch(RuntimeException failure) {
      session=null;context=-1;for(var ticket:new ArrayList<>(requests))finish(ticket);for(var page:pages)page.expire();
    }
  }
  private boolean current(Ticket ticket) {return live()&&!ticket.done&&requests.contains(ticket)&&ticket.sameDocument();}
  private void finish(Ticket ticket) {
    if(!requests.remove(ticket))return;ticket.cancel();navigation.remove(ticket.origin.document,ticket);
    if(pages.stream().noneMatch(page -> page.origin==ticket.origin))ticket.origin.dispose();
  }
  private Ticket begin(Origin origin) {
    if(!origin.anchor.isValid()){inform("查询位置已失效，请从当前符号重新发起。");return null;}
    var ticket=new Ticket(origin);requests.add(ticket);armTimeout(ticket);return ticket;
  }
  private void armTimeout(Ticket ticket) {
    if(ticket.timeout!=null)ticket.timeout.cancel(false);
    ticket.timeout=AppExecutorUtil.getAppScheduledExecutorService().schedule(() -> ApplicationManager.getApplication().invokeLater(() -> {
      if(current(ticket)){finish(ticket);inform("后端分析尚未完成，请稍后重试；没有将分析中视为无符号。");}
    }),15,TimeUnit.SECONDS);
  }
  private void send(Ticket ticket,JsonObject envelope,Consumer<JsonObject> success,Runnable failed) {
    ticket.request=BackendConnection.get(project).query(envelope.toString(),raw -> {
      if(!current(ticket))return;
      try {
        var reply=JsonParser.parseString(raw).getAsJsonObject();
        if(!reply.has("requestId")||!ticket.id.equals(reply.get("requestId").getAsString())||reply.get("documentVersion").getAsLong()!=ticket.version)
          throw new IllegalStateException("Reply/document identity mismatch");
        var next=reply.get("session").getAsString();long epoch=reply.get("contextVersion").getAsLong();
        if(session!=null&&(!session.equals(next)||epoch<context))throw new IllegalStateException("Reply context expired");
        session=next;context=Math.max(context,epoch);ticket.resultSession=next;ticket.resultEpoch=epoch;success.accept(reply);
      } catch(RuntimeException failure) {LOG.warn("AnotherFSM response rejected",failure);finish(ticket);failed.run();inform("无法确认当前结果，已拒绝导航；请刷新。");}
    },failure -> {if(current(ticket)){finish(ticket);failed.run();LOG.warn("AnotherFSM request failed",failure);inform("后端不可用或请求已取消；请等待重连后重试。");}});
  }
  public void run(Editor editor,String action,String operation,boolean implementation) {
    var previous=navigation.remove(editor.getDocument());if(previous!=null)finish(previous);
    var origin=new Origin(editor,action,operation,implementation);var ticket=begin(origin);if(ticket==null){origin.dispose();return;}
    navigation.put(origin.document,ticket);
    send(ticket,ticket.envelope,reply -> present(ticket,reply),() -> {});
  }
  private void present(Ticket ticket,JsonObject reply) {
    String status=reply.get("status").getAsString();
    LOG.info("AFSM navigation status="+status+" request="+ticket.id+" contextVersion="+reply.get("contextVersion"));
    if(status.equals("hosts")) {
      chooseHost(ticket,reply);return;
    }
    if(status.equals("type")) {
      var navigation=ticket.envelope.deepCopy();navigation.addProperty("action","navigateType");
      navigation.add("session",reply.get("session"));navigation.add("contextVersion",reply.get("contextVersion"));navigation.add("target",reply.get("target"));
      send(ticket,navigation,result -> {finish(ticket);if(!"navigationDispatched".equals(result.get("status").getAsString()))inform(result.get("reason").getAsString());},() -> {});
      return;
    }
    var locations=locations(reply);
    boolean references=!ticket.origin.action.equals("resolve");
    if(status.equals("dsl")&&locations.size()==1&&!references) {validate(ticket,reply,locations.get(0),() -> {});return;}
    if(status.equals("locations")||status.equals("dsl")) {
      if(!references&&!locations.isEmpty()){
        var reason=reply.get("reason").getAsString();if(!reason.isBlank())inform(reason);
        choose(ticket,reply,locations);return;
      }
      var page=new ResultPage(ticket.origin,references);pages.add(page);page.replace(reply);finish(ticket);return;
    }
    finish(ticket);inform(reply.get("reason").getAsString());
  }
  private record HostChoice(JsonObject value) {
    public String toString() {return value.get("label").getAsString();}
  }
  private void chooseHost(Ticket ticket,JsonObject reply) {
    if(ticket.timeout!=null)ticket.timeout.cancel(false);
    var choices=new ArrayList<HostChoice>();for(var host:reply.getAsJsonArray("hosts"))choices.add(new HostChoice(host.getAsJsonObject()));
    var popup=JBPopupFactory.getInstance().createPopupChooserBuilder(choices).setTitle("选择消费项目 / TFM（仅本次显示）").setRequestFocus(true)
        .setItemChosenCallback(choice -> {
          if(!current(ticket))return;ticket.popup=null;armTimeout(ticket);
          ticket.envelope.addProperty("host",choice.value().get("id").getAsString());
          ticket.envelope.add("session",reply.get("session"));ticket.envelope.add("contextVersion",reply.get("contextVersion"));
          send(ticket,ticket.envelope,result -> present(ticket,result),() -> {});
        }).createPopup();
    ticket.popup=popup;
    popup.addListener(new JBPopupListener(){public void onClosed(LightweightWindowEvent event){if(!event.isOk())finish(ticket);}});
    popup.showCenteredInCurrentWindow(project);
  }
  private void choose(Ticket ticket,JsonObject reply,java.util.List<Location> choices) {
    if(ticket.timeout!=null)ticket.timeout.cancel(false);
    var popup=JBPopupFactory.getInstance().createPopupChooserBuilder(choices)
        .setTitle(reply.get("reason").getAsString()).setRequestFocus(true)
        .setItemChosenCallback(location -> {
          if(!current(ticket))return;ticket.popup=null;armTimeout(ticket);
          validate(ticket,reply,location,() -> {});
        }).createPopup();
    ticket.popup=popup;
    // Rider closes an accepted popup before invoking its chosen-item callback. Only cancellation
    // ends the ticket here; otherwise onClosed would cancel the very navigation the user accepted.
    popup.addListener(new JBPopupListener(){public void onClosed(LightweightWindowEvent event){if(!event.isOk())finish(ticket);}});
    var file=FileDocumentManager.getInstance().getFile(ticket.origin.document);
    var editor=file==null?null:FileEditorManager.getInstance(project).getSelectedTextEditor();
    if(editor!=null&&editor.getDocument()==ticket.origin.document)popup.showInBestPositionFor(editor);
    else popup.showCenteredInCurrentWindow(project);
  }
  private static java.util.List<Location> locations(JsonObject reply) {
    var rows=new ArrayList<Location>();if(reply.has("locations")&&!reply.get("locations").isJsonNull())
      for(var location:reply.getAsJsonArray("locations"))rows.add(new Location(location.getAsJsonObject()));return rows;
  }
  private void validate(Ticket ticket,JsonObject result,Location location,Runnable failed) {
    var request=ticket.envelope.deepCopy();request.addProperty("action","validateLocation");request.addProperty("queryAction",ticket.origin.action);
    request.add("session",result.get("session"));request.add("contextVersion",result.get("contextVersion"));request.add("selected",location.value);
    send(ticket,request,reply -> {
      if(!reply.get("status").getAsString().equals("dsl")){finish(ticket);failed.run();inform(reply.get("reason").getAsString());return;}
      var rows=locations(reply);if(rows.size()!=1)throw new IllegalStateException("Exact source target missing");
      select(rows.get(0),ticket.origin);finish(ticket);
    },failed);
  }
  private void select(Location location,Origin origin) {
    var value=location.value;var path=value.get("documentId").getAsString();
    // Non-physical, unsaved editors have a real VirtualFile/Document but no local filesystem path.
    var file=path.equals(origin.path)?FileDocumentManager.getInstance().getFile(origin.document):LocalFileSystem.getInstance().findFileByPath(path);
    if(file==null||!file.isValid())throw new IllegalStateException("Target file unavailable");
    var document=FileDocumentManager.getInstance().getDocument(file);
    if(document==null||!document.getText().equals(value.get("text").getAsString()))throw new IllegalStateException("Target snapshot changed");
    var span=value.getAsJsonObject("span");int start=span.get("start").getAsInt(),end=start+span.get("length").getAsInt();
    if(start<0||end<start||end>document.getTextLength())throw new IllegalStateException("Invalid source range");
    var editor=FileEditorManager.getInstance(project).openTextEditor(new OpenFileDescriptor(project,file,start),true);
    if(editor==null)throw new IllegalStateException("Target editor unavailable");
    editor.getCaretModel().moveToOffset(start);editor.getSelectionModel().setSelection(start,end);editor.getScrollingModel().scrollToCaret(ScrollType.MAKE_VISIBLE);
  }

  private final class ResultPage implements Disposable {
    final Origin origin;final boolean references;final DefaultListModel<Location> model=new DefaultListModel<>();
    final JBList<Location> list=new JBList<>(model);final JLabel summary=new JLabel();final Content content;
    String session;long epoch=-1;boolean expired,closed;Ticket active;JsonObject result;
    ResultPage(Origin origin,boolean references) {
      this.origin=origin;this.references=references;var panel=new JPanel(new BorderLayout());var toolbar=new JPanel(new BorderLayout());
      var refresh=new JButton("刷新");refresh.addActionListener(event -> refresh());toolbar.add(summary,BorderLayout.CENTER);toolbar.add(refresh,BorderLayout.EAST);
      list.setName("AnotherFSM.references");list.setSelectionMode(ListSelectionModel.SINGLE_SELECTION);
      list.addMouseListener(new MouseAdapter(){public void mouseClicked(MouseEvent event){if(event.getClickCount()==2)navigate();}});
      list.getInputMap().put(KeyStroke.getKeyStroke(KeyEvent.VK_ENTER,0),"anotherfsm.navigate");
      list.getActionMap().put("anotherfsm.navigate",new AbstractAction(){public void actionPerformed(ActionEvent event){navigate();}});
      panel.add(toolbar,BorderLayout.NORTH);panel.add(new JScrollPane(list),BorderLayout.CENTER);
      content=ContentFactory.getInstance().createContent(panel,"AnotherFSM",true);content.setDisposer(this);
      var window=ToolWindowManager.getInstance(project).getToolWindow("AnotherFSM");
      if(window==null)throw new IllegalStateException("AnotherFSM tool window is unavailable");
      window.getContentManager().addContent(content);window.getContentManager().setSelectedContent(content);window.show(null);
    }
    void replace(JsonObject reply) {
      result=reply.deepCopy();session=reply.get("session").getAsString();epoch=reply.get("contextVersion").getAsLong();expired=false;
      model.clear();for(var location:locations(reply))model.addElement(location);
      content.setDisplayName(references?"AnotherFSM 引用":"AnotherFSM 选择导航目标");
      summary.setText(reply.get("reason").getAsString()+" · "+model.size()+" 个源码位置");
    }
    void expire() {
      if(closed)return;expired=true;content.setDisplayName("结果已过期 — AnotherFSM");summary.setText("结果已过期，请刷新；旧位置不能导航。");
      if(active!=null){finish(active);active=null;}
    }
    void refresh() {
      if(closed)return;if(active!=null)finish(active);summary.setText("分析中（未将分析中当作无引用）…");
      var ticket=begin(origin);active=ticket;if(ticket==null){expire();return;}
      send(ticket,ticket.envelope,reply -> {
        if(active!=ticket)return;
        String status=reply.get("status").getAsString();
        if(status.equals("locations")||status.equals("dsl"))replace(reply);else {expire();inform(reply.get("reason").getAsString());}
        finish(ticket);active=null;
      },this::expire);
    }
    void navigate() {
      var location=list.getSelectedValue();if(location==null||closed)return;
      if(expired){inform("结果已过期，请先刷新；没有跳转旧位置。");return;}
      if(active!=null)finish(active);active=begin(origin);if(active==null){expire();return;}
      validate(active,result,location,this::expire);
    }
    public void dispose() {if(closed)return;closed=true;pages.remove(this);if(active!=null)finish(active);origin.dispose();model.clear();result=null;}
  }
  public void dispose() {
    disposed=true;for(var ticket:new ArrayList<>(requests))finish(ticket);for(var page:new ArrayList<>(pages))page.dispose();navigation.clear();
  }
  /** Declarative registration keeps tool-window construction inside Rider's UI lifecycle. */
  public static final class Factory implements ToolWindowFactory {
    public void createToolWindowContent(Project project,ToolWindow window) {}
  }
}
