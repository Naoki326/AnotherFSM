package anotherfsm;

import com.google.gson.*;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.actionSystem.*;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.components.Service;
import com.intellij.openapi.diagnostic.Logger;
import com.intellij.openapi.editor.*;
import com.intellij.openapi.editor.event.*;
import com.intellij.openapi.fileEditor.FileDocumentManager;
import com.intellij.openapi.project.*;
import com.intellij.openapi.util.Disposer;
import com.intellij.notification.*;
import com.intellij.util.concurrency.AppExecutorUtil;
import java.util.*;
import java.util.concurrent.*;

public final class NavigationActions {
  private NavigationActions() {}
  private static final Logger LOG = Logger.getInstance("AnotherFSM");
  private static boolean script(Editor editor) {
    var file=FileDocumentManager.getInstance().getFile(editor.getDocument());
    if(file==null) return false;
    var name=file.getName().toLowerCase(Locale.ROOT);
    return name.endsWith(".fsm")||name.endsWith(".fsmscript");
  }
  public static class Declaration extends DumbAwareAction {
    protected boolean implementation() { return false; }
    public ActionUpdateThread getActionUpdateThread() { return ActionUpdateThread.BGT; }
    public void update(AnActionEvent event) {
      var editor=event.getData(CommonDataKeys.EDITOR);
      event.getPresentation().setEnabledAndVisible(event.getProject()!=null && editor!=null && script(editor));
    }
    public void actionPerformed(AnActionEvent event) {
      var project=event.getProject(); var editor=event.getData(CommonDataKeys.EDITOR);
      if(project!=null && editor!=null && script(editor)) project.getService(Client.class).navigate(editor,implementation());
    }
  }
  public static final class Implementation extends Declaration {
    protected boolean implementation() { return true; }
  }

  @Service(Service.Level.PROJECT)
  public static final class Client implements Disposable {
    private final Project project;
    private final Map<Document,Pending> pending=new HashMap<>();
    private String session;
    private long contextVersion=-1;
    private static final class Pending {
      final Editor editor; final Document document; final String text; final long version; final String id=UUID.randomUUID().toString();
      Disposable request; ScheduledFuture<?> timeout;
      Pending(Editor editor) { this.editor=editor; document=editor.getDocument(); text=document.getText(); version=document.getModificationStamp(); }
      void cancel() { if(request!=null) Disposer.dispose(request); if(timeout!=null) timeout.cancel(false); }
    }
    public Client(Project project) {
      this.project=project;
      BackendConnection.get(project).onInvalidated(this::invalidated);
      EditorFactory.getInstance().getEventMulticaster().addDocumentListener(new DocumentListener() {
        public void documentChanged(DocumentEvent event) {
          Runnable invalidate=() -> { var active=pending.get(event.getDocument());
            if(active!=null && active.version!=event.getDocument().getModificationStamp()) cancel(event.getDocument()); };
          if(ApplicationManager.getApplication().isDispatchThread()) invalidate.run();
          else ApplicationManager.getApplication().invokeLater(invalidate);
        }
      },this);
      EditorFactory.getInstance().addEditorFactoryListener(new EditorFactoryListener() {
        public void editorReleased(EditorFactoryEvent event) { cancel(event.getEditor().getDocument()); }
      },this);
    }
    private void cancel(Document document) {
      var old=pending.remove(document); if(old!=null) old.cancel();
    }
    private void inform(String reason) {
      if(!project.isDisposed()) NotificationGroupManager.getInstance().getNotificationGroup("AnotherFSM").createNotification(reason,NotificationType.INFORMATION).notify(project);
    }
    private void invalidated(String raw) {
      if(project.isDisposed()) return;
      try {
        var event=JsonParser.parseString(raw).getAsJsonObject();
        var next=event.get("session").getAsString();
        if(session!=null && !session.equals(next)) { pending.values().forEach(Pending::cancel); pending.clear(); contextVersion=-1; }
        session=next; contextVersion=Math.max(contextVersion,event.get("contextVersion").getAsLong());
      } catch(RuntimeException failure) {
        // Transport reconnect markers have no semantic generation; invalidate all in-flight requests.
        pending.values().forEach(Pending::cancel); pending.clear(); session=null; contextVersion=-1;
      }
    }
    public void navigate(Editor editor,boolean implementation) {
      cancel(editor.getDocument());
      var operation=new Pending(editor); pending.put(operation.document,operation);
      var file=FileDocumentManager.getInstance().getFile(operation.document);
      var request=new JsonObject();
      request.addProperty("action","resolve"); request.addProperty("path",file==null?"untitled":file.getPath());
      request.addProperty("text",operation.text); request.addProperty("documentVersion",operation.version);
      request.addProperty("offset",editor.getCaretModel().getOffset()); request.addProperty("requestId",operation.id);
      request.addProperty("implementation",implementation);
      request.addProperty("session",session); request.addProperty("contextVersion",contextVersion);
      operation.timeout=AppExecutorUtil.getAppScheduledExecutorService().schedule(() -> ApplicationManager.getApplication().invokeLater(() -> {
        if(pending.get(operation.document)==operation) { cancel(operation.document); inform("AnotherFSM 后端分析尚未完成，请稍后重试；没有将分析中视为无符号。"); }
      }),15,TimeUnit.SECONDS);
      send(operation,request);
    }
    private boolean current(Pending operation) {
      return !project.isDisposed() && !operation.editor.isDisposed() && pending.get(operation.document)==operation
          && operation.document.getModificationStamp()==operation.version && operation.document.getText().equals(operation.text);
    }
    private void finish(Pending operation) { if(pending.get(operation.document)==operation) cancel(operation.document); }
    private void send(Pending operation,JsonObject request) {
      operation.request=BackendConnection.get(project).query(request.toString(),raw -> {
        if(!current(operation)) return;
        try {
          var reply=JsonParser.parseString(raw).getAsJsonObject();
          if(!reply.has("requestId") || !operation.id.equals(reply.get("requestId").getAsString())
              || operation.version!=reply.get("documentVersion").getAsLong()) { finish(operation); inform("AnotherFSM 回复版本不匹配，已拒绝导航。"); return; }
          var responseSession=reply.get("session").getAsString(); var responseVersion=reply.get("contextVersion").getAsLong();
          if(session!=null && (!session.equals(responseSession)||responseVersion<contextVersion)) { finish(operation); inform("AnotherFSM 结果已过期，请重新导航。"); return; }
          session=responseSession; contextVersion=Math.max(contextVersion,responseVersion);
          var status=reply.get("status").getAsString();
          LOG.info("AFSM32 navigation status="+status+" request="+operation.id+" documentVersion="+operation.version+" contextVersion="+responseVersion);
          if(status.equals("dsl")) {
            var span=reply.getAsJsonObject("span"); int start=span.get("start").getAsInt(), end=start+span.get("length").getAsInt();
            if(start<0 || end<start || end>operation.document.getTextLength()) throw new IllegalStateException("Invalid source range");
            operation.editor.getCaretModel().moveToOffset(start); operation.editor.getSelectionModel().setSelection(start,end);
            operation.editor.getScrollingModel().scrollToCaret(ScrollType.MAKE_VISIBLE); finish(operation);
          } else if(status.equals("type")) {
            var navigation=request.deepCopy(); navigation.addProperty("action","navigateType");
            navigation.addProperty("session",responseSession); navigation.addProperty("contextVersion",responseVersion); navigation.add("target",reply.get("target"));
            send(operation,navigation);
          } else { finish(operation); if(!status.equals("navigationDispatched")) inform("AnotherFSM："+reply.get("reason").getAsString()); }
        } catch(RuntimeException failure) { finish(operation); LOG.warn("AnotherFSM response rejected",failure); inform("AnotherFSM 无法确认当前结果，已拒绝导航。"); }
      },failure -> { if(current(operation)) { finish(operation); LOG.warn("AnotherFSM request failed",failure); inform("AnotherFSM 后端不可用或请求已取消；请等待重连后重试。"); } });
    }
    public void dispose() { pending.values().forEach(Pending::cancel); pending.clear(); }
  }
}
