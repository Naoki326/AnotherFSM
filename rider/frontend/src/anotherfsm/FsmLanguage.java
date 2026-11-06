package anotherfsm;

import com.intellij.lang.*;
import com.intellij.lexer.*;
import com.intellij.extapi.psi.*;
import com.intellij.openapi.fileTypes.*;
import com.intellij.openapi.project.Project;
import com.intellij.psi.*;
import com.intellij.psi.tree.*;
import javax.swing.Icon;
import java.util.regex.Pattern;

/** Editor tokenization only. All declaration, binding and scope decisions live in .NET. */
public final class FsmLanguage {
  static final Language LANGUAGE = new Language("AnotherFSM") {};
  static final IElementType TOKEN = new IElementType("TOKEN", LANGUAGE);
  static final IElementType COMMENT = new IElementType("COMMENT", LANGUAGE);
  static final IElementType STRING = new IElementType("STRING", LANGUAGE);
  static final IFileElementType FILE = new IFileElementType(LANGUAGE);
  static final Pattern TOKENS = Pattern.compile("//[^\\r\\n]*|/\\*[\\s\\S]*?(?:\\*/|$)|\"[^\"]*(?:\"|$)|\\s+|[^\\s]");

  public static final class Type extends LanguageFileType {
    public static final Type INSTANCE = new Type();
    private Type() { super(LANGUAGE); }
    public String getName() { return "AnotherFSM"; }
    public String getDescription() { return "AnotherFSM script"; }
    public String getDefaultExtension() { return "fsm"; }
    public Icon getIcon() { return null; }
  }
  public static final class Lex extends LexerBase {
    CharSequence buffer = ""; int end, start, stop; IElementType type;
    public void start(CharSequence b, int s, int e, int state) { buffer=b; end=e; stop=s; advance(); }
    public int getState() { return 0; }
    public IElementType getTokenType() { return type; }
    public int getTokenStart() { return start; }
    public int getTokenEnd() { return stop; }
    public CharSequence getBufferSequence() { return buffer; }
    public int getBufferEnd() { return end; }
    public void advance() {
      start=stop;
      if(start>=end) { type=null; return; }
      var match=TOKENS.matcher(buffer).region(start,end);
      if(!match.lookingAt()) throw new IllegalStateException("Tokenizer did not advance");
      stop=match.end(); var value=match.group();
      type=value.isBlank()?TokenType.WHITE_SPACE:value.startsWith("//")||value.startsWith("/*")?COMMENT:
          value.startsWith("\"")?STRING:TOKEN;
    }
  }
  public static final class File extends PsiFileBase {
    public File(FileViewProvider provider) { super(provider, LANGUAGE); }
    public FileType getFileType() { return Type.INSTANCE; }
  }
  public static final class Parser implements ParserDefinition {
    public Lexer createLexer(Project project) { return new Lex(); }
    public PsiParser createParser(Project project) { return (root,builder) -> {
      var mark=builder.mark(); while(!builder.eof()) builder.advanceLexer(); mark.done(root); return builder.getTreeBuilt();
    }; }
    public IFileElementType getFileNodeType() { return FILE; }
    public TokenSet getCommentTokens() { return TokenSet.create(COMMENT); }
    public TokenSet getStringLiteralElements() { return TokenSet.create(STRING); }
    public PsiElement createElement(ASTNode node) { return new ASTWrapperPsiElement(node); }
    public PsiFile createFile(FileViewProvider provider) { return new File(provider); }
  }
}
