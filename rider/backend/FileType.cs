using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Parsing;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.Text;
using JetBrains.UI.Icons;

namespace AnotherFSM.Rider;

[ProjectFileTypeDefinition(Name)]
public sealed class FsmFileType : KnownProjectFileType
{
    public new const string Name = "AnotherFSM";
    public new static FsmFileType Instance { get; private set; } = null!;
    private FsmFileType() : base(Name, "AnotherFSM script", new[] { ".fsm", ".FSMScript" }) { }
}

[LanguageDefinition(Name)]
public sealed class FsmLanguage : KnownLanguage
{
    public new const string Name = "AnotherFSM";
    public static FsmLanguage Instance { get; private set; } = null!;
    public FsmLanguage() : base(Name, "AnotherFSM") { }
}

[ProjectFileType(typeof(FsmFileType))]
public sealed class FsmFileService(FsmFileType type, ILanguageManager languages) : ProjectFileLanguageService(type)
{
    protected override PsiLanguageType PsiLanguageType => FsmLanguage.Instance;
    public override IconId Icon => null!;
    // This is only a broad word prefilter for the host's native search, not DSL semantics.
    public override ILexerFactory GetMixedLexerFactory(ISolution solution, IBuffer buffer, IPsiSourceFile sourceFile) =>
        languages.GetService<LanguageService>(CSharpLanguage.Instance).GetPrimaryLexerFactory();
    public override IPsiSourceFileProperties GetPsiProperties(IProjectFile file, IPsiSourceFile source, IsCompileService compile) => new Properties();
    private sealed class Properties : IPsiSourceFileProperties
    {
        public bool ShouldBuildPsi => false;
        public bool IsGeneratedFile => false;
        public bool IsICacheParticipant => true;
        public bool ProvidesCodeModel => false;
        public bool IsNonUserFile => false;
        public IEnumerable<string> GetPreImportedNamespaces() => Array.Empty<string>();
        public string GetDefaultNamespace() => "";
        public ICollection<PreProcessingDirective> GetDefines() => Array.Empty<PreProcessingDirective>();
    }
}
