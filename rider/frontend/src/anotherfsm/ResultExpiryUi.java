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

/** Presentation only: mark the real native Find tab; never resolves symbols or changes its rows. */
@Service(Service.Level.PROJECT)
public final class ResultExpiryUi implements Disposable {
  private final Project project;
  private final LifetimeDefinition lifetime=new LifetimeDefinition();
  private final Map<Content,Marker> markers=new IdentityHashMap<>();
  private boolean disposed;
  private long generation;
  private String session;
  private long context=-1;
  private static final class Marker {
    final Content content; final String display,tab,description; final LifetimeDefinition scope;
    boolean refreshing,seenRunning; long generation;
    Marker(Content content,LifetimeDefinition scope) {
      this.content=content; this.scope=scope; display=content.getDisplayName();tab=content.getTabName();description=content.getDescription();
    }
    void label(String prefix) { content.setDisplayName(prefix+display);content.setTabName(prefix+tab);content.setDescription(prefix+display+"；请使用原生 Rerun 刷新"); }
    void restore() { content.setDisplayName(display);content.setTabName(tab);content.setDescription(description); }
  }
  public ResultExpiryUi(Project project) { this.project=project;BackendConnection.get(project).onInvalidated(this::invalidated); }
  private void later(Runnable action) { ApplicationManager.getApplication().invokeLater(() -> {if(!disposed&&!project.isDisposed())action.run();},ModalityState.any()); }
  private void invalidated(String raw) {
    if(disposed||project.isDisposed())return;
    var event=JsonParser.parseString(raw).getAsJsonObject();
    boolean disconnected=event.has("transportInvalidated");
    if(disconnected) { generation++;session=null;context=-1; }
    else {
      var next=event.get("session").getAsString();long version=event.get("contextVersion").getAsLong();
      if(!Objects.equals(session,next)||version>context)generation++;
      session=next;context=Math.max(context,version);
    }
    var titles=new HashSet<String>();
    if(event.has("expiredReferences"))for(var title:event.getAsJsonArray("expiredReferences"))titles.add(title.getAsString());
    if(!disconnected&&titles.isEmpty())return;
    var window=ToolWindowManager.getInstance(project).getToolWindow("Find");if(window==null)return;
    for(var content:window.getContentManager().getContents()) {
      var data=DataManager.getInstance().getDataContext(content.getComponent());
      var view=UsageView.USAGE_VIEW_KEY.getData(data);if(!(view instanceof UsageViewImpl nativeView))continue;
      var sessions=Collections.newSetFromMap(new IdentityHashMap<RdFindUsagesSession,Boolean>());
      for(var target:nativeView.getTargets())if(target instanceof FrontendUsageTarget nativeTarget)sessions.add(nativeTarget.getSession());
      if(sessions.size()!=1)continue;
      var nativeSession=sessions.iterator().next();
      if(!disconnected&&!titles.contains(nativeSession.getTitle()))continue;
      var marker=markers.get(content);
      if(marker==null) {
        var scope=lifetime.createNested();
        marker=new Marker(content,scope);markers.put(content,marker);var state=marker;
        Disposer.register(content,() -> {scope.terminate(true);later(() -> markers.remove(content));});
        var protocol=nativeSession.getProtocol();
        if(protocol!=null&&scope.getStatus()==LifetimeStatus.Alive)protocol.getScheduler().queue(() -> {
          if(scope.getStatus()!=LifetimeStatus.Alive)return Unit.INSTANCE;
          nativeSession.getReRun().advise(scope,ignored -> {later(() -> {
            if(scope.getStatus()!=LifetimeStatus.Alive||markers.get(content)!=state)return;
            state.refreshing=true;state.seenRunning=false;state.generation=generation;state.label("刷新中 — ");
          });return Unit.INSTANCE;});
          nativeSession.isSearchComplete().advise(scope,complete -> {later(() -> {
            if(scope.getStatus()!=LifetimeStatus.Alive||markers.get(content)!=state)return;
            if(!state.refreshing)return;
            if(!complete)state.seenRunning=true;
            else if(state.seenRunning&&state.generation==generation){state.refreshing=false;state.restore();}
          });return Unit.INSTANCE;});
          return Unit.INSTANCE;
        });
      }
      marker.refreshing=false;marker.label("结果已过期 — ");
    }
  }
  public void dispose() { disposed=true;lifetime.terminate(true);markers.clear(); }
  public static final class Start implements ProjectActivity {
    public Object execute(Project project,Continuation<? super Unit> continuation) {project.getService(ResultExpiryUi.class);return Unit.INSTANCE;}
  }
}
