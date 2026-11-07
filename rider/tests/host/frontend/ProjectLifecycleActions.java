package anotherfsm.acceptance;

import com.intellij.ide.DataManager;
import com.intellij.ide.projectView.ProjectView;
import com.intellij.openapi.actionSystem.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.util.ActionCallback;
import com.intellij.openapi.vfs.VfsUtilCore;
import com.intellij.openapi.wm.ToolWindowManager;
import com.intellij.util.ui.tree.TreeUtil;
import com.jetbrains.rider.model.RdProjectDescriptor;
import com.jetbrains.rider.model.RdUnloadProjectDescriptor;
import com.jetbrains.rider.projectView.ProjectModelDataKeys;
import com.jetbrains.rider.projectView.views.SolutionViewEntityOwner;
import com.jetbrains.rider.projectView.views.solutionExplorer.SolutionExplorerViewPane;
import com.jetbrains.rider.projectView.workspace.ProjectModelEntity;
import java.nio.file.Path;
import java.util.Map;
import java.util.UUID;
import javax.swing.*;
import javax.swing.tree.TreePath;

/** Test-only adapter: acts on existing, selected native project entities; never constructs a project or descriptor. */
public final class ProjectLifecycleActions {
  private ProjectLifecycleActions() {}
  public record Selection(JTree tree,SolutionExplorerViewPane pane,ProjectModelEntity entity) {
    public Map<String,String> observation() {
      var descriptor=entity.getDescriptor();String state=descriptor instanceof RdProjectDescriptor loaded?loaded.getState().toString():
          descriptor instanceof RdUnloadProjectDescriptor unloaded?unloaded.getState().toString():"not-project";
      return Map.of("name",entity.getName(),"url",entity.getUrl()==null?"no physical URL":entity.getUrl().getUrl(),"descriptor",descriptor.getClass().getName(),"state",state);
    }
  }
  public static Selection select(Project project,Path target) {return lookup(project,target,true,null);}
  public static Selection select(Project project,Path target,UUID guid) {return lookup(project,target,true,guid);}
  public static Selection inspect(Project project,Path target) {return lookup(project,target,false,null);}
  private static boolean matches(ProjectModelEntity entity,String expected,UUID guid) {
    if(entity.getUrl()!=null&&VfsUtilCore.urlToPath(entity.getUrl().getUrl()).replace('\\','/').equalsIgnoreCase(expected))return true;
    return guid!=null&&(entity.getDescriptor() instanceof RdProjectDescriptor loaded&&guid.equals(loaded.getOriginalGuid())
        ||entity.getDescriptor() instanceof RdUnloadProjectDescriptor unloaded&&guid.equals(unloaded.getGuid()));
  }
  private static Selection lookup(Project project,Path target,boolean select,UUID guid) {
    ProjectView.getInstance(project).changeView("SolutionExplorer");
    var pane=SolutionExplorerViewPane.Companion.tryGetInstance(project);if(pane==null)return null;
    var tree=pane.getTree();if(tree==null)return null;
    var windows=ToolWindowManager.getInstance(project);
    for(var id:windows.getToolWindowIds()) {
      var window=windows.getToolWindow(id);if(window!=null&&SwingUtilities.isDescendingFrom(tree,window.getComponent()))window.show(null);
    }
    if(!tree.isShowing())return null;
    String expected=target.toAbsolutePath().normalize().toString().replace('\\','/');
    for(int row=0;row<tree.getRowCount()&&row<256;row++) {
      TreePath path=tree.getPathForRow(row);if(path==null)continue;Object item=TreeUtil.getLastUserObject(path);
      if(item instanceof SolutionViewEntityOwner owner) {
        var entity=owner.getEntity();if(entity==null)continue;
        var descriptor=entity.getDescriptor();boolean isProject=descriptor instanceof RdProjectDescriptor||descriptor instanceof RdUnloadProjectDescriptor;
        if(isProject&&matches(entity,expected,guid)) {
          if(!select)return new Selection(tree,pane,entity);
          tree.setSelectionPath(path);tree.scrollPathToVisible(path);
          var context=DataManager.getInstance().getDataContext(tree);
          var selected=ProjectModelDataKeys.INSTANCE.getPROJECT_MODEL_ENTITY_ARRAY().getData(context);
          if(CommonDataKeys.PROJECT.getData(context)!=project||selected==null||selected.length!=1||!matches(selected[0],expected,guid))return null;
          return new Selection(tree,pane,selected[0]);
        }
        if(isProject)continue;
      }
      tree.expandRow(row);
    }
    return null;
  }
  public static ActionCallback perform(Selection selection,String id) {
    var action=ActionManager.getInstance().getAction(id);
    if(action==null)throw new IllegalStateException("Native project action not registered: "+id);
    // These actions require BGT update. tryToExecute(false) owns their real component context/update.
    return ActionManager.getInstance().tryToExecute(action,null,selection.tree(),selection.pane().getPopupPlaceFromRootNode(),false);
  }
}
