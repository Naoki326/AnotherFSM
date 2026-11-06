using System.Text.Json;
using AnotherFSM.Rider;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;

namespace AnotherFSM.Acceptance;

/// <summary>Controlled target-SDK seam checks. This does not claim editor or performance acceptance.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class GraphSdkProbe
{
    private Timer? timer;
    private int queued;
    public GraphSdkProbe(Lifetime lifetime, ISolution solution, IPsiServices psi, ScriptDocuments scripts, ScriptIndex index)
    {
        var directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        var start = DateTime.UtcNow;
        timer = new Timer(_ => {
            if (!lifetime.IsAlive || DateTime.UtcNow - start > TimeSpan.FromMinutes(2)) { timer?.Dispose(); return; }
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            solution.Locks.QueueReadLock(lifetime, "AnotherFSM.SDKGraphProbe", () => {
                try
                {
                    if (!psi.Files.AllDocumentsAreCommitted) return;
                    var modules = scripts.Modules().Where(module => module.ContainingProjectModule is IProject project && project.Name == "Graphs").ToArray();
                    if (modules.Length != 1) return;
                    var module = modules[0];
                    var catalog = index.Catalog(module);
                    var types = catalog.Where(type => type.Key == "GraphKey").ToArray();
                    var bare = catalog.Where(type => type.Key == "GraphBareKey").ToArray();
                    if (types.Length != 1 || bare.Length != 1) return; // Host is not ready, not a zero-result pass.
                    var epoch = index.Epoch;
                    var roots = scripts.Roots(module).Select(ScriptDocuments.Id).ToArray();
                    var checks = new List<object>();
                    bool passed = true;
                    void Check(string name, bool result, object detail)
                    {
                        passed &= result;
                        checks.Add(new { name, passed = result, detail });
                    }
                    foreach (var path in new[] { "Graphs/Flow/graph.fsm", "Graphs/Items/none.FSMScript", "Graphs/Items/content.fsm",
                        "Graphs/Items/resource.FSMScript", "SharedGraphs/linked.FSMScript" })
                        Check("project-item: " + path, roots.Any(root => root.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)), roots);
                    Check("physical directory does not imply project membership", !roots.Any(root => root.Contains("/Loose/")
                        || root.Contains("/Decoys/") || root.EndsWith("/DisplayOnly/LinkedChecker.fsm")), roots);
                    var linked = scripts.Roots(module).Single(file => ScriptDocuments.Id(file).EndsWith("/SharedGraphs/linked.FSMScript", StringComparison.OrdinalIgnoreCase));
                    var dependency = scripts.Import(module, ScriptDocuments.Id(linked), "LinkedChecker");
                    Check("linked import uses physical directory", dependency != null && ScriptDocuments.Id(dependency)
                        .EndsWith("/SharedGraphs/LinkedChecker.fsm", StringComparison.OrdinalIgnoreCase), dependency == null ? "missing" : ScriptDocuments.Id(dependency));
                    Check("import-only file is not fabricated project membership", !roots.Any(root => root.EndsWith("/SharedGraphs/LinkedChecker.fsm", StringComparison.OrdinalIgnoreCase)), roots);
                    var metadata = TypeCatalog.BranchMetadata(psi, types[0].Identity, 1);
                    Check("explicit index metadata has actual source", metadata != null && metadata.DocumentId.EndsWith("/Graphs/Nodes.cs", StringComparison.OrdinalIgnoreCase)
                        && metadata.Text.Substring(metadata.Span.Start, metadata.Span.Length).Contains("FSMNode(\"GraphKey\""), metadata ?? (object)"unavailable");
                    Check("unknown index does not fabricate metadata", TypeCatalog.BranchMetadata(psi, types[0].Identity, 999) == null, 999);
                    Check("bare key does not borrow enum metadata", TypeCatalog.BranchMetadata(psi, bare[0].Identity, 3) == null, "GraphBareKey / index 3");
                    if (epoch != index.Epoch) return;
                    File.WriteAllText(Path.Combine(directory, "sdk-graph-probe.json"), JsonSerializer.Serialize(new {
                        status = passed ? "passed" : "failed", seam = "target SDK public ScriptDocuments / TypeCatalog; not UI acceptance",
                        backendPid = Environment.ProcessId, epoch, module = module.GetPersistentID(), roots, checks
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    timer?.Dispose();
                }
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(directory, "sdk-graph-probe.json"), JsonSerializer.Serialize(new { status = "failed", error = error.ToString() }));
                    timer?.Dispose();
                }
                finally { Interlocked.Exchange(ref queued, 0); }
            });
        }, null, 1000, 1000);
        lifetime.OnTermination(() => timer?.Dispose());
    }
}
