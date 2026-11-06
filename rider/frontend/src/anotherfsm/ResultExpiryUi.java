package anotherfsm;

import com.google.gson.*;
import com.intellij.ide.DataManager;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.application.ModalityState;
import com.intellij.openapi.components.Service;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.startup.ProjectActivity;
import com.intellij.openapi.util.Disposer;
import com.intellij.openapi.wm.ToolWindowManager;
import com.intellij.ui.content.Content;
import com.intellij.usages.UsageView;
import com.intellij.usages.impl.UsageViewImpl;
import com.jetbrains.rd.ide.model.RdFindUsagesSession;
import com.jetbrains.rd.util.lifetime.*;
import com.jetbrains.rdclient.usages.FrontendUsageTarget;
import java.util.*;
import kotlin.Unit;
import kotlin.coroutines.Continuation;

/** Native ListId identifies the page; a backend query epoch identifies its current result set. */
@Service(Service.Level.PROJECT)
public final class ResultExpiryUi implements Disposable {
  private final Project project;
  private final LifetimeDefinition lifetime=new LifetimeDefinition();
  private final Map<Content,Marker> markers=new IdentityHashMap<>();
  private boolean disposed;
  private String session;
  private long context=-1;
  private static final class Marker {
    final Content content; final String listId,display,tab,description; final LifetimeDefinition scope;
    long check; Disposable request;
    Marker(Content content,String listId,LifetimeDefinition scope) {
      this.content=content;this.listId=listId;this.scope=scope;
      display=content.getDisplayName();tab=content.getTabName();description=content.getDescription();
    }
    void label(String prefix) { content.setDisplayName(prefix+display);content.setTabName(prefix+tab);content.setDescription(prefix+display+"；请使用原生 Rerun 刷新"); }
    void restore() { content.setDisplayName(display);content.setTabName(tab);content.setDescription(description); }
    void cancelCheck() { check++;if(request!=null){Disposer.dispose(request);request=null;} }
  }
  public ResultExpiryUi(Project project) { this.project=project;BackendConnection.get(project).onInvalidated(this::invalidated); }
  private void later(Runnable action) { ApplicationManager.getApplication().invokeLater(() -> {if(!disposed&&!project.isDisposed())action.run();},ModalityState.any()); }
  private boolean current(Marker marker) {return !disposed&&!project.isDisposed()&&marker.scope.getStatus()==LifetimeStatus.Alive&&markers.get(marker.content)==marker;}
  private void invalidated(String raw) {
    if(disposed||project.isDisposed())return;
    var event=JsonParser.parseString(raw).getAsJsonObject();
    boolean disconnected=event.has("transportInvalidated");
    if(disconnected) {session=null;context=-1;}
    else {
      var next=event.get("session").getAsString();long version=event.get("contextVersion").getAsLong();
      if(!Objects.equals(session,next)){session=next;context=version;}else context=Math.max(context,version);
    }
    var ids=new HashSet<String>();
    if(event.has("expiredReferences"))for(var query:event.getAsJsonArray("expiredReferences"))ids.add(query.getAsJsonObject().get("listId").getAsString());
    if(!disconnected&&ids.isEmpty())return;
    var window=ToolWindowManager.getInstance(project).getToolWindow("Find");if(window==null)return;
    for(var content:window.getContentManager().getContents()) {
      var view=UsageView.USAGE_VIEW_KEY.getData(DataManager.getInstance().getDataContext(content.getComponent()));
      if(!(view instanceof UsageViewImpl nativeView))continue;
      var sessions=Collections.newSetFromMap(new IdentityHashMap<RdFindUsagesSession,Boolean>());
      for(var target:nativeView.getTargets())if(target instanceof FrontendUsageTarget nativeTarget)sessions.add(nativeTarget.getSession());
      if(sessions.size()!=1)continue;
      var nativeSession=sessions.iterator().next();String listId=nativeSession.getUsages().getListId();
      if(!disconnected&&!ids.contains(listId))continue;
      var marker=markers.get(content);
      if(marker==null||!marker.listId.equals(listId))marker=track(content,nativeSession,listId);
      if(disconnected){marker.cancelCheck();marker.label("结果已过期 — ");}
      // Read the live query epoch, not the epoch/title of a possibly delayed notification.
      inspect(marker);
    }
  }
  private Marker track(Content content,RdFindUsagesSession nativeSession,String listId) {
    var previous=markers.remove(content);if(previous!=null){previous.cancelCheck();previous.scope.terminate(true);previous.restore();}
    var scope=lifetime.createNested();var state=new Marker(content,listId,scope);markers.put(content,state);
    Disposer.register(content,() -> {state.cancelCheck();scope.terminate(true);later(() -> markers.remove(content,state));});
    var protocol=nativeSession.getProtocol();
    if(protocol!=null&&scope.getStatus()==LifetimeStatus.Alive)protocol.getScheduler().queue(() -> {
      if(scope.getStatus()!=LifetimeStatus.Alive)return Unit.INSTANCE;
      nativeSession.getReRun().advise(scope,ignored -> {later(() -> {
        if(current(state)){state.cancelCheck();state.label("刷新中 — ");}
      });return Unit.INSTANCE;});
      nativeSession.isSearchComplete().advise(scope,complete -> {later(() -> {
        if(current(state)&&complete)inspect(state);
      });return Unit.INSTANCE;});
      return Unit.INSTANCE;
    });
    return state;
  }
  private void inspect(Marker marker) {
    if(!current(marker))return;
    marker.cancelCheck();long check=marker.check;
    var request=new JsonObject();String id=UUID.randomUUID().toString();
    request.addProperty("action","referenceStatus");request.addProperty("listId",marker.listId);
    request.addProperty("path","untitled");request.addProperty("text","");request.addProperty("documentVersion",0);
    request.addProperty("offset",0);request.addProperty("requestId",id);
    marker.request=BackendConnection.get(project).query(request.toString(),raw -> {
      if(!current(marker)||marker.check!=check)return;
      var reply=JsonParser.parseString(raw).getAsJsonObject();
      if(!reply.has("requestId")||!id.equals(reply.get("requestId").getAsString()))return;
      var responseSession=reply.get("session").getAsString();long responseContext=reply.get("contextVersion").getAsLong();
      if(session!=null&&(!session.equals(responseSession)||responseContext<context))return;
      if(!"referenceStatus".equals(reply.get("status").getAsString())||!reply.has("reference")||reply.get("reference").isJsonNull()){
        marker.label("结果已过期 — ");return;
      }
      var status=reply.getAsJsonObject("reference");
      if(!marker.listId.equals(status.get("listId").getAsString()))return;
      if(status.get("updating").getAsBoolean()){marker.label("刷新中 — ");return;}
      if(status.get("queryEpoch").getAsLong()<status.get("contextVersion").getAsLong())marker.label("结果已过期 — ");
      else marker.restore();
    },failure -> {if(current(marker)&&marker.check==check)marker.label("结果已过期 — ");});
  }
  public void dispose() {disposed=true;for(var marker:markers.values())marker.cancelCheck();lifetime.terminate(true);markers.clear();}
  public static final class Start implements ProjectActivity {
    public Object execute(Project project,Continuation<? super Unit> continuation) {project.getService(ResultExpiryUi.class);return Unit.INSTANCE;}
  }
}
