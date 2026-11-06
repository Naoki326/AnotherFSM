using System.Text.Json;
using AnotherFSM.Rider;
using JetBrains.Application.Parts;
using JetBrains.Application.Progress;
using JetBrains.Application.Threading;
using JetBrains.DocumentManagers;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Occurrences;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.ExtensionsAPI;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Psi.Impl.Search;
using JetBrains.ReSharper.Psi.Search;

namespace AnotherFSM.Acceptance;

/// <summary>
/// Read-only target-SDK checks, independent of GraphSdkProbe. Expected red before the #33 repairs:
/// a single-file query leaks module-wide keys; repeating a callback loses results; project-item
/// fallback documents differ from Rider's real project documents. No editor/business execution.
/// </summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class SearchScopeProbe
{
    private Timer? timer;
    private int queued;
    private int finished;

    public SearchScopeProbe(Lifetime lifetime, ISolution solution, IPsiServices psi,
        ScriptDocuments scripts, ScriptIndex index, DocumentManager documents)
    {
        var directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        var start = DateTime.UtcNow;
        var waitingFor = "committed PSI and loaded Graphs fixture";
        var cancellations = 0;
        void Finish(object report)
        {
            if (Interlocked.Exchange(ref finished, 1) != 0) return;
            timer?.Dispose();
            File.WriteAllText(Path.Combine(directory, "sdk-search-scope.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        timer = new Timer(_ => {
            if (!lifetime.IsAlive || Volatile.Read(ref finished) != 0) { timer?.Dispose(); return; }
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            solution.Locks.QueueReadLock(lifetime, "AnotherFSM.SDKSearchScopeProbe", () => {
                var checks = new List<object>();
                var importOnlyEvidence = new List<object>();
                try
                {
                    // A readiness timeout is never a zero-result pass. No SDK/UI mutation is needed.
                    if (DateTime.UtcNow - start > TimeSpan.FromMinutes(2))
                    {
                        Finish(new { status = "blocked", seam = "SDK only; not UI acceptance", waitingFor, cancellations });
                        return;
                    }
                    if (!psi.Files.AllDocumentsAreCommitted) return;
                    waitingFor = "one Graphs consuming module and GraphWorker C# PSI";
                    var modules = scripts.Modules().Where(module => module.ContainingProjectModule is IProject project
                        && project.Name == "Graphs").ToArray();
                    if (modules.Length != 1) return;
                    var module = modules[0];
                    var project = (IProject)module.ContainingProjectModule;
                    var targets = psi.Symbols.GetSymbolScope(module, true, true).GetTypeElementsByCLRName("Graphs.GraphWorker");
                    if (targets.Length != 1 || !targets[0].IsValid()
                        || index.Catalog(module).Count(type => type.Key == "GraphKey") != 1) return;
                    var target = targets[0];
                    var projectItems = project.GetAllProjectFiles(file => ScriptDocuments.IsScriptName(file.Name))
                        .Where(file => file.IsValid() && file.Location.ExistsFile).ToArray();
                    string[] required = ["Graphs/Flow/graph.fsm", "Graphs/Items/none.FSMScript",
                        "Graphs/Items/content.fsm", "Graphs/Items/resource.FSMScript", "SharedGraphs/linked.FSMScript"];
                    waitingFor = "real Graphs script project items";
                    if (required.Any(path => !projectItems.Any(item => EndsWith(PathId(item), path)))) return;

                    var epoch = index.Epoch;
                    var roots = scripts.Roots(module);
                    var passed = true;
                    void Check(string name, bool result, object detail)
                    {
                        passed &= result;
                        checks.Add(new { name, passed = result, detail });
                    }
                    // Check all actual script project items, not merely the five readiness sentinels.
                    // Never create an independent path document as the test's source of truth.
                    foreach (var item in projectItems)
                    {
                        var path = PathId(item);
                        var realDocument = documents.GetOrCreateDocument(item);
                        var rootViews = roots.Where(file => SamePath(ScriptDocuments.Id(file), path)).ToArray();
                        var foundViews = scripts.Find(path).Where(file => file.PsiModule.GetPersistentID() == module.GetPersistentID()).ToArray();
                        Check("Roots binds real project document: " + path,
                            rootViews.Length == 1 && ReferenceEquals(rootViews[0].Document, realDocument),
                            new { rootCount = rootViews.Length, sameDocuments = rootViews.Select(file => ReferenceEquals(file.Document, realDocument)).ToArray() });
                        Check("Find binds real project document: " + path,
                            foundViews.Length == 1 && ReferenceEquals(foundViews[0].Document, realDocument),
                            new { foundCount = foundViews.Length, sameDocuments = foundViews.Select(file => ReferenceEquals(file.Document, realDocument)).ToArray() });
                        Check("CurrentText agrees with real project document (no buffer edit): " + path,
                            scripts.CurrentText(path) == realDocument.GetText(),
                            new { unsavedEditExercised = false, projectTextLength = realDocument.GetTextLength() });
                    }

                    var fileA = roots.SingleOrDefault(file => EndsWith(ScriptDocuments.Id(file), "Graphs/Items/none.FSMScript"));
                    Check("file A is available for restricted query", fileA != null, roots.Select(ScriptDocuments.Id).ToArray());
                    if (fileA != null)
                    {
                        var fixtureText = fileA.Document.GetText();
                        // Independent known fixture oracle: two factory keys in these two declarations.
                        // Locate offsets by fixed declaration text, not by invoking semantic analysis.
                        const string entry = "def NoneEntry(GraphKey)";
                        const string exit = "def NoneExit(GraphKey)";
                        var entryStart = fixtureText.IndexOf(entry, StringComparison.Ordinal);
                        var exitStart = fixtureText.IndexOf(exit, StringComparison.Ordinal);
                        if (entryStart < 0 || exitStart < 0)
                            throw new InvalidOperationException("Scope fixture changed: expected NoneEntry / NoneExit declarations");
                        var expectedOffsets = new[] { entryStart + "def NoneEntry(".Length, exitStart + "def NoneExit(".Length }.Order().ToArray();
                        bool ExactFileA(Found[] found) => found.Length == 2 && found.All(row => row.valid
                            && SamePath(row.file, ScriptDocuments.Id(fileA)) && row.length == "GraphKey".Length && row.text == "GraphKey")
                            && found.Select(row => row.start).Order().SequenceEqual(expectedOffsets);
                        void SearchCheck(string name, Action<Collector> run)
                        {
                            try
                            {
                                var consumer = new Collector();
                                run(consumer);
                                var found = consumer.Snapshot().Select(Describe).ToArray();
                                Check(name, ExactFileA(found), new { expectedFile = ScriptDocuments.Id(fileA), expectedOffsets, found });
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception error) { Check(name, false, new { error = error.ToString() }); }
                        }

                        // SDK Finder is intentionally exercised, not simulated by a direct plugin call.
                        // If the host rejects this context, record a failed Finder check, not a UI pass.
                        var domain = psi.SearchDomainFactory.CreateSearchDomain(fileA);
                        SearchCheck("Finder single-file domain returns only the two real file-A keys", consumer =>
                            psi.SingleThreadedFinder.FindReferences(target, domain, consumer, NullProgressIndicator.Create()));

                        // The public factory/callback contract is a separate, explicitly labelled seam.
                        // Separate consumers allow host-level deduplication, but not permanent plugin suppression.
                        var factory = new FsmSearcherFactory(psi.SearchDomainFactory);
                        var searcher = factory.CreateReferenceSearcher(new DeclaredElementsSet(target),
                            new ReferenceSearcherParameters(null, false, false));
                        Check("public factory supplies GraphWorker searcher", searcher != null, "FsmSearcherFactory.CreateReferenceSearcher");
                        if (searcher != null)
                        {
                            SearchCheck("callback #1 contributes file A only", consumer => searcher.ProcessProjectItem(fileA, consumer));
                            SearchCheck("same searcher callback #2 remains repeatable", consumer => searcher.ProcessProjectItem(fileA, consumer));
                        }
                    }
                    // Import-only Module references belong to this consuming context without becoming
                    // project items. Test their own source domain; never recover them via another file's callback.
                    var linked = roots.Single(file => EndsWith(ScriptDocuments.Id(file), "SharedGraphs/linked.FSMScript"));
                    var imported = scripts.Import(module, ScriptDocuments.Id(linked), "LinkedChecker");
                    Check("linked physical import supplies its exact source", imported != null
                        && EndsWith(ScriptDocuments.Id(imported), "SharedGraphs/LinkedChecker.fsm"),
                        imported == null ? "missing" : ScriptDocuments.Id(imported));
                    if (imported != null)
                    {
                        var importPath = ScriptDocuments.Id(imported);
                        Check("import-only source does not fabricate project membership",
                            !projectItems.Any(item => SamePath(PathId(item), importPath))
                            && !roots.Any(file => SamePath(ScriptDocuments.Id(file), importPath)), importPath);
                        var importText = imported.Document.GetText();
                        const string actualEntry = "def ActualEntry(GraphKey)";
                        var declarationStart = importText.IndexOf(actualEntry, StringComparison.Ordinal);
                        if (declarationStart < 0)
                            throw new InvalidOperationException("Import-only fixture changed: expected ActualEntry(GraphKey)");
                        var expectedOffset = declarationStart + "def ActualEntry(".Length;
                        var importFactory = new FsmSearcherFactory(psi.SearchDomainFactory);
                        var importDomain = psi.SearchDomainFactory.CreateSearchDomain(imported);
                        var wholeSolution = psi.SearchDomainFactory.CreateSearchDomain(solution, false);
                        void Observe(string name, Func<object> read)
                        {
                            try { importOnlyEvidence.Add(new { name, value = read() }); }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception error) { importOnlyEvidence.Add(new { name, error = error.ToString() }); }
                        }
                        object DomainEvidence(ISearchDomain domain)
                        {
                            var files = domain.EnumerateFiles().ToArray();
                            var matches = files.Where(file => SamePath(ScriptDocuments.Id(file), importPath)).ToArray();
                            return new { domain.IsEmpty, fileCount = files.Length, importPathMatches = matches.Length,
                                sameSource = matches.Any(file => ReferenceEquals(file, imported)),
                                matchingSourceTypes = matches.Select(file => file.GetType().FullName).ToArray(),
                                dump = domain.DumpToString() };
                        }
                        Observe("source language and cache properties", () => new {
                            file = importPath, sourceType = imported.GetType().FullName,
                            languageType = imported.LanguageType.ToString(), languageTypeClass = imported.LanguageType.GetType().FullName,
                            primaryPsiLanguage = imported.PrimaryPsiLanguage.ToString(), primaryPsiLanguageClass = imported.PrimaryPsiLanguage.GetType().FullName,
                            imported.Properties.IsICacheParticipant, imported.Properties.ShouldBuildPsi,
                            inModuleSourceFiles = module.SourceFiles.Any(file => SamePath(ScriptDocuments.Id(file), importPath))
                        });
                        Observe("public GetLanguages categories used by Finder factory dispatch", () => new {
                            dominantLanguages = psi.Files.GetLanguages(imported, PsiLanguageCategories.Dominant).Select(language => new {
                                name = language.ToString(), type = language.GetType().FullName, compatible = importFactory.IsCompatibleWithLanguage(language) }).ToArray(),
                            allLanguages = psi.Files.GetLanguages(imported, PsiLanguageCategories.All).Select(language => new {
                                name = language.ToString(), type = language.GetType().FullName, compatible = importFactory.IsCompatibleWithLanguage(language) }).ToArray()
                        });
                        Observe("public word-index candidate evidence (false cache participation alone is not a skip)", () => {
                            var words = importFactory.GetAllPossibleWordsInFile(target).ToArray();
                            var allFactoryWords = FinderUtil.GetAllPossibleWordsInFile(target, psi);
                            return new { words, allFactoryWords, wordIndexType = psi.WordIndex.GetType().FullName,
                                canContainFactoryWords = psi.WordIndex.CanContainAnyWord(imported, words),
                                canContainGraphKey = psi.WordIndex.CanContainAnyWord(imported, new[] { "GraphKey" }),
                                canContainAnyCombinedFactoryWord = allFactoryWords == null ? (bool?)null : psi.WordIndex.CanContainAnyWord(imported, allFactoryWords) };
                        });
                        Observe("public word-index enumeration (absence alone does not imply CanContainAnyWord=false)", () => new {
                            indexedImportPaths = psi.WordIndex.GetFilesContainingAnyWords(importFactory.GetAllPossibleWordsInFile(target))
                                .Where(file => SamePath(ScriptDocuments.Id(file), importPath)).Select(ScriptDocuments.Id).ToArray()
                        });
                        Observe("explicit import source domain", () => DomainEvidence(importDomain));
                        Observe("plugin declared domain", () => DomainEvidence(importFactory.GetDeclaredElementSearchDomain(target)));
                        Observe("SDK combined target domain from all searcher factories", () => DomainEvidence(target.GetSearchDomain(false)));
                        Observe("SDK whole-solution domain (not a captured UI scope)", () => DomainEvidence(wholeSolution));
                        Observe("whole-solution intersect plugin declared domain", () =>
                            DomainEvidence(wholeSolution.Intersect(importFactory.GetDeclaredElementSearchDomain(target))));
                        Observe("whole-solution intersect SDK combined target domain", () => DomainEvidence(wholeSolution.Intersect(target.GetSearchDomain(false))));
                        Observe("SDK whole-solution after public Finder narrowing", () => DomainEvidence(
                            FinderUtil.NarrowSearchDomain(wholeSolution, new IDeclaredElement[] { target }, SearchPattern.FIND_USAGES, psi)));

                        Found[]? explicitFound = null;
                        Observe("Finder explicit import-only source query", () => {
                            var consumer = new Collector();
                            psi.SingleThreadedFinder.FindReferences(target, importDomain, consumer, NullProgressIndicator.Create());
                            explicitFound = consumer.Snapshot().Select(Describe).ToArray();
                            return new { found = explicitFound };
                        });
                        Check("Finder import-only source domain returns its one real GraphKey range",
                            explicitFound is { Length: 1 } && explicitFound[0].valid && SamePath(explicitFound[0].file, importPath)
                            && explicitFound[0].start == expectedOffset && explicitFound[0].length == "GraphKey".Length
                            && explicitFound[0].text == "GraphKey",
                            new { expectedFile = importPath, expectedOffset, expectedLength = "GraphKey".Length, found = explicitFound });
                        // The SDK's processedFiles may come from node visits, not complete file callbacks.
                        // Exercise that public contract without fabricating a PSI tree for this text-only language.
                        var phased = importFactory.CreateImplicitUsageSearchers(SearchPattern.FIND_USAGES, new IDeclaredElement[] { target }).Single();
                        var unconfirmed = phased.TryCreateSecondPhaseInfo(psi.SearchDomainFactory, importDomain, new HashSet<IPsiSourceFile> { imported });
                        Check("SDK processedFiles alone does not suppress an unconfirmed import source",
                            unconfirmed.Searcher != null && unconfirmed.SearchDomain?.EnumerateFiles().Any(file => SamePath(ScriptDocuments.Id(file), importPath)) == true,
                            unconfirmed.SearchDomain == null ? "no additional domain" : DomainEvidence(unconfirmed.SearchDomain));
                        var firstPhaseConsumer = new Collector();
                        phased.ProcessProjectItem(imported, firstPhaseConsumer);
                        var firstPhaseRows = firstPhaseConsumer.Snapshot().Select(Describe).ToArray();
                        Check("first-phase whole import callback contributes its real key", firstPhaseRows.Length == 1
                            && firstPhaseRows[0].valid && firstPhaseRows[0].start == expectedOffset, firstPhaseRows);
                        var completedPhase = phased.TryCreateSecondPhaseInfo(psi.SearchDomainFactory, importDomain, null);
                        Check("only a successful whole-file callback suppresses the second phase",
                            completedPhase.SearchDomain == null || !completedPhase.SearchDomain.EnumerateFiles().Any(file => SamePath(ScriptDocuments.Id(file), importPath)),
                            completedPhase.SearchDomain == null ? "covered by first phase" : DomainEvidence(completedPhase.SearchDomain));
                        // Diagnostic comparisons only: neither substitutes for the Finder regression above.
                        Observe("public factory import-only callback (not Finder/UI acceptance)", () => {
                            var consumer = new Collector();
                            var searcher = importFactory.CreateReferenceSearcher(new DeclaredElementsSet(target),
                                new ReferenceSearcherParameters(null, false, false));
                            if (searcher == null) throw new InvalidOperationException("No GraphWorker searcher");
                            searcher.ProcessProjectItem(imported, consumer);
                            return new { found = consumer.Snapshot().Select(Describe).ToArray() };
                        });
                        Found[]? wholeImport = null;
                        Observe("Finder SDK whole-solution query (not native UI scope)", () => {
                            var consumer = new Collector();
                            psi.SingleThreadedFinder.FindReferences(target, wholeSolution, consumer, NullProgressIndicator.Create());
                            var rows = consumer.Snapshot().Select(Describe).ToArray();
                            wholeImport = rows.Where(row => SamePath(row.file, importPath)).ToArray();
                            return new { totalResults = rows.Length, importOnlyResults = wholeImport };
                        });
                        Check("Finder whole-solution query includes the one real import-only key without broadening a restricted file scope",
                            wholeImport is { Length: 1 } && wholeImport[0].valid && wholeImport[0].start == expectedOffset
                            && wholeImport[0].length == "GraphKey".Length && wholeImport[0].text == "GraphKey",
                            new { expectedFile = importPath, expectedOffset, found = wholeImport });
                    }
                    if (index.Epoch != epoch) throw new OperationCanceledException("Probe snapshot expired");
                    Finish(new {
                        status = passed ? "passed" : "failed",
                        seam = "public target SDK Finder single-file domain + factory callbacks + project document identity; NOT native UI acceptance",
                        backendPid = Environment.ProcessId, epoch, module = module.GetPersistentID(), cancellations,
                        projectItems = projectItems.Select(PathId).ToArray(), checks, importOnlyEvidence,
                        limitations = new[] { "No unsaved buffer modification: host UI must verify CurrentText after edits.",
                            "No ProcessElement subtree-range assertion; no cancellation/recovery race reproduction.",
                            "No cross-project/TFM, external JSON, completion/docs or budget acceptance." }
                    });
                }
                catch (OperationCanceledException) { cancellations++; waitingFor = "stable query epoch after cancellation"; }
                catch (Exception error)
                {
                    Finish(new { status = "failed", seam = "SDK only; not UI acceptance", error = error.ToString(), checks, importOnlyEvidence, cancellations });
                }
                finally { Interlocked.Exchange(ref queued, 0); }
            });
        }, null, 1000, 1000);
        lifetime.OnTermination(() => timer?.Dispose());
    }

    private static string PathId(IProjectFile file) => file.Location.FullPath.Replace('\\', '/');
    private static bool SamePath(string a, string b) => StringComparer.OrdinalIgnoreCase.Equals(a, b);
    private static bool EndsWith(string path, string suffix) => path.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase);

    // No private FsmKeyResult fields, fixture-only production API, or invented SDK stubs.
    private static Found Describe(FindResult result)
    {
        if (new FsmOccurrenceProvider().MakeOccurrence(result) is not RangeOccurrence occurrence)
            return new("<not a plugin range occurrence>", -1, 0, "", false);
        var range = occurrence.GetDocumentRange();
        var textRange = range.TextRange;
        var text = range.Document.GetText();
        var valid = range.IsValid() && occurrence.IsValid && textRange.StartOffset >= 0 && textRange.EndOffset <= text.Length;
        return new(ScriptDocuments.Id(occurrence.SourceFile), textRange.StartOffset, textRange.Length,
            valid ? text.Substring(textRange.StartOffset, textRange.Length) : "<invalid range>", valid);
    }

    private sealed record Found(string file, int start, int length, string text, bool valid);

    private sealed class Collector : IFindResultConsumer<FindResult>
    {
        private readonly List<FindResult> results = new();
        public FindResult Build(FindResult result) => result;
        public FindExecution Merge(FindResult result)
        {
            lock (results) results.Add(result);
            return FindExecution.Continue;
        }
        public FindResult[] Snapshot() { lock (results) return results.ToArray(); }
    }
}
