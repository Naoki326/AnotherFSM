using System.Text.Json;
using AnotherFSM.Rider;
using AnotherFSM.Semantics;
using JetBrains.Application.Parts;
using JetBrains.Application.Settings;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.ExternalSources.MetadataViewer;
using JetBrains.ReSharper.ExternalSources.ReSharperIntegration;
using JetBrains.ReSharper.Feature.Services.ExternalSource;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Core;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Core.Impl;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Pdb;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Resources.Shell;

namespace AnotherFSM.Acceptance;

/// <summary>
/// Boundary journey's opt-in, read-only SDK observer. The frontend writes a run-correlated
/// observation request only AFTER checking the actual sandbox launcher. No policy writes,
/// navigation, restore/build, assembly execution, downloads or authentication occur here.
/// Independently prepare/open tests/fixtures/boundary/Boundary.sln; do not add it to Complex.sln.
/// </summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class ComplexBoundaryDriver
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private Timer? timer;
    private int queued;

    public ComplexBoundaryDriver(Lifetime lifetime, ISolution solution, IPsiServices psi, ScriptDocuments scripts, ScriptIndex index)
    {
        var directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var started = DateTime.UtcNow;
        long sequence = 0;
        string? lastRun = null, lastState = null;
        var history = new List<object>();
        timer = new Timer(_ => {
            if (!lifetime.IsAlive || DateTime.UtcNow - started > TimeSpan.FromMinutes(20)) { timer?.Dispose(); return; }
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            solution.Locks.QueueReadLock(lifetime, "AnotherFSM.ComplexBoundary.ReadOnly", () => {
                string? runId = null;
                try
                {
                    var commandPath = Path.Combine(directory, "complex-boundary-observe.json");
                    if (!File.Exists(commandPath)) return;
                    using var command = JsonDocument.Parse(File.ReadAllText(commandPath));
                    var request = command.RootElement;
                    if (request.GetProperty("operation").GetString() != "observe-only") return;
                    runId = request.GetProperty("runId").GetString();
                    if (!Guid.TryParse(runId, out var parsedRunId)) return;
                    if (runId != lastRun) { history.Clear(); lastState = null; lastRun = runId; }
                    var root = Path.GetFullPath(request.GetProperty("root").GetString()!).Replace('\\', '/').TrimEnd('/');
                    // An environment variable/command is correlation, NOT sandbox isolation proof.
                    // This component is read-only even if somebody forges a command.
                    var modules = scripts.Modules().Where(module => (BindingSettings.ProjectPath(module) ?? "")
                        .Equals(root + "/Host/BoundaryHost.csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
                    var caches = solution.GetComponent<IPsiCachesState>();
                    var ready = psi.Files.AllDocumentsAreCommitted && caches.IsInitialUpdateFinished.Value && caches.IsPrimaryIdle.Value;
                    var epoch = index.Epoch;
                    var views = modules.Select(module => {
                        var catalog = ready ? index.Catalog(module) : Array.Empty<NodeType>();
                        var bindings = new[] { "BoundaryReferenceKey", "BoundaryFrameworkKey", "BoundaryProjectKey", "HostDllKey" }
                            .Select(key => {
                                var binding = NodeBindings.Resolve(new("Probe", key, new(0, 5), new(6, key.Length), false), catalog);
                                return new { key, state = binding.State.ToString(), binding.Reason, binding.Candidates };
                            }).ToArray();
                        return new { id = module.GetPersistentID(), project = BindingSettings.ProjectPath(module),
                            framework = BindingSettings.Framework(module), tfmIdentity = module.TargetFrameworkId.UniqueString,
                            references = psi.Modules.GetModuleReferences(module).Select(reference => new {
                                id = reference.Module.GetPersistentID(), project = BindingSettings.ProjectPath(reference.Module), reference.ExternAliases
                            }).ToArray(), bindings };
                    }).ToArray();
                    object? policy = null;
                    if (ready && modules.Length == 1)
                    {
                        var candidates = index.Catalog(modules[0]).Where(type => type.Key == "HostDllKey").ToArray();
                        if (candidates.Length == 1 && TypeCatalog.Resolve(psi, candidates[0].Identity) is ICompiledElement compiled)
                        {
                            var bound = Shell.Instance.GetComponent<ISettingsStore>().BindToContextTransient(ContextRange.ApplicationWide);
                            var service = solution.GetComponent<IExternalSourcesService>();
                            var options = solution.GetComponent<TranslatorOptions>();
                            var mode = solution.GetComponent<INavigationModeProvider>();
                            var info = compiled.ToNavigationInfo();
                            policy = new {
                                identity = candidates[0].Identity,
                                rawMode = bound.GetValue((ExternalSourcesSettings value) => value.NavigationMode).ToString(),
                                rawBodies = bound.GetValue((DecompilerSettings value) => value.DecompileMethodBodies),
                                consent = bound.GetValue((DecompilerSettings value) => value.DecompilerLegalNoticeAccepted),
                                effectiveMode = mode.GetNavigationMode().ToString(), effectiveBodies = options.GetDecompileMethodBodiesValue(bound),
                                active = Shell.Instance.GetComponent<ExternalSourcesActivation>().Active.Value,
                                forceCookie = ExternalSourcesForceSourceCookie.Check(),
                                downloadPdb = bound.GetValue((PdbNavigationSettings value) => value.Download),
                                usePdb = bound.GetValue((PdbNavigationSettings value) => value.UseSymbolFiles),
                                sourceLink = bound.GetValue((PdbNavigationSettings value) => value.SourceLinkV3),
                                // Exact same whitelist as production. No unknown provider calls, no generation.
                                providers = service.OrderedProviders.Where(provider => provider.GetType() == typeof(DecompiledSourcesExternalSourcesProvider)
                                    || provider.GetType() == typeof(MetadataViewerExternalSourcesProvider)).Select(provider => new {
                                        id = provider.Id, runtimeType = provider.GetType().FullName,
                                        applicable = provider.IsApplicableForNavigation(info, ignoreOptions: false)
                                    }).ToArray(),
                                modeType = mode.GetType().FullName, translatorType = options.GetType().FullName
                            };
                        }
                    }
                    var state = views.Select(view => new { view.id, view.framework, view.bindings }).ToArray();
                    var signature = JsonSerializer.Serialize(new { ready, state });
                    if (signature != lastState)
                    {
                        history.Add(new { sampledUtc = DateTime.UtcNow, epoch, structureVersion = index.StructureVersion, ready, modules = state });
                        if (history.Count > 64) history.RemoveAt(0);
                        lastState = signature;
                    }
                    var marker = Environment.GetEnvironmentVariable("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");
                    var markerConfigured = !string.IsNullOrWhiteSpace(marker) && Path.IsPathFullyQualified(marker);
                    Write(directory, new { runId, backendPid = Environment.ProcessId, seq = ++sequence, sampledUtc = DateTime.UtcNow,
                        session = index.Session, epoch, structureVersion = index.StructureVersion,
                        coherent = epoch == index.Epoch, ready, committed = psi.Files.AllDocumentsAreCommitted,
                        initialUpdate = caches.IsInitialUpdateFinished.Value, primaryIdle = caches.IsPrimaryIdle.Value,
                        modules = views, history, policy, sentinel = new { configured = markerConfigured, exists = markerConfigured && File.Exists(marker) },
                        seam = "read-only public SDK observation, never a host action or source identity proof" });
                }
                catch (OperationCanceledException) { /* Retry; cancellation is NOT an external generation-failure test. */ }
                catch (IOException) { /* Atomic command replacement / next sample. */ }
                catch (JsonException) { /* Never consume partially written command data. */ }
                catch (Exception error) { Write(directory, new { runId, backendPid = Environment.ProcessId, seq = ++sequence, error = error.GetType().FullName }); }
                finally { Interlocked.Exchange(ref queued, 0); }
            });
        }, null, 500, 500);
        lifetime.OnTermination(() => timer?.Dispose());
    }

    private static void Write(string directory, object value)
    {
        var target = Path.Combine(directory, "complex-boundary-observation.json");
        try
        {
            File.WriteAllText(target + ".tmp", JsonSerializer.Serialize(value, Json));
            File.Move(target + ".tmp", target, true);
        }
        catch (IOException) { /* Windows readers can briefly hold the old output; next bounded sample retries. */ }
        catch (UnauthorizedAccessException) { /* Atomic replacement can also report a sharing denial; no stale sample is promoted. */ }
    }
}
