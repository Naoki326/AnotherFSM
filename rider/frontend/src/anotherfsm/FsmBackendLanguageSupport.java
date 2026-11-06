package anotherfsm;

import com.intellij.psi.PsiFile;
import com.jetbrains.rider.editors.RiderCustomBackendLanguageSupport;

/** Lets Rider own editor/shared-document binding and unsaved-buffer synchronization. */
public final class FsmBackendLanguageSupport implements RiderCustomBackendLanguageSupport {
  public boolean isAvailable(PsiFile file) {
    return file.getFileType() instanceof FsmLanguage.Type;
  }
}
