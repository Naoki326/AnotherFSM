package anotherfsm;

import com.intellij.openapi.actionSystem.*;
import com.intellij.openapi.editor.Editor;
import com.intellij.openapi.fileEditor.FileDocumentManager;
import com.intellij.openapi.project.DumbAwareAction;
import java.util.Locale;

/** Actions contain no name resolution: every name/instance/scope decision comes from the shared core. */
public final class NavigationActions {
  private NavigationActions() {}
  private static boolean script(Editor editor) {
    var file=FileDocumentManager.getInstance().getFile(editor.getDocument());
    if(file==null) return false;
    var name=file.getName().toLowerCase(Locale.ROOT);
    return name.endsWith(".fsm")||name.endsWith(".fsmscript");
  }
  public static class Declaration extends DumbAwareAction {
    protected String action() { return "resolve"; }
    protected String operation() { return "declaration"; }
    protected boolean implementation() { return false; }
    public ActionUpdateThread getActionUpdateThread() { return ActionUpdateThread.BGT; }
    public void update(AnActionEvent event) {
      var editor=event.getData(CommonDataKeys.EDITOR);
      event.getPresentation().setEnabledAndVisible(event.getProject()!=null && editor!=null && script(editor));
    }
    public void actionPerformed(AnActionEvent event) {
      var editor=event.getData(CommonDataKeys.EDITOR);var project=event.getProject();
      if(project!=null && editor!=null && script(editor))
        project.getService(SemanticUi.class).run(editor,action(),operation(),implementation());
    }
  }
  public static final class Implementation extends Declaration { protected boolean implementation() { return true; } }
  public static final class References extends Declaration { protected String action() { return "references"; } }
  public static final class TemplateReferences extends Declaration { protected String action() { return "templateReferences"; } }
  public static final class ModuleDefinition extends Declaration { protected String operation() { return "moduleDefinition"; } }
  public static final class Entry extends Declaration { protected String operation() { return "entry"; } }
  public static final class Internal extends Declaration { protected String operation() { return "internal"; } }
  public static final class BranchMetadata extends Declaration { protected String operation() { return "branchMetadata"; } }
}
