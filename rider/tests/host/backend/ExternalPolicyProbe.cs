using System.Text.Json;
using AnotherFSM.Rider;
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

/// <summary>Separate, read-only policy evidence. Never generates external text, activates services, searches or changes a setting.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class ExternalPolicyProbe
{
    private Timer? timer;
    private int queued;
    public ExternalPolicyProbe(Lifetime lifetime, ISolution solution, IPsiServices psi, ScriptDocuments scripts, ScriptIndex index)
    {
        var directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        var start = DateTime.UtcNow;
        var samples = new List<object>();
        long sequence = 0;
        timer = new Timer(_ => {
            if (!lifetime.IsAlive || DateTime.UtcNow - start > TimeSpan.FromMinutes(20)) { timer?.Dispose(); return; }
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            solution.Locks.QueueReadLock(lifetime, "AnotherFSM.ExternalPolicyObserver", () => {
                try
                {
                    if (!psi.Files.AllDocumentsAreCommitted) return;
                    var modules = scripts.Modules().Where(module => module.ContainingProjectModule is IProject project && project.Name == "HostA").ToArray();
                    if (modules.Length != 1) return;
                    var candidates = index.Catalog(modules[0]).Where(type => type.Key == "HostDllKey").ToArray();
                    if (candidates.Length != 1 || TypeCatalog.Resolve(psi, candidates[0].Identity) is not ICompiledElement compiled) return;
                    var bound = Shell.Instance.GetComponent<ISettingsStore>().BindToContextTransient(ContextRange.ApplicationWide);
                    var service = solution.GetComponent<IExternalSourcesService>();
                    var options = solution.GetComponent<TranslatorOptions>();
                    var mode = solution.GetComponent<INavigationModeProvider>();
                    var caches = solution.GetComponent<IPsiCachesState>();
                    var info = compiled.ToNavigationInfo();
                    var providers = service.OrderedProviders.Where(provider => provider.GetType() == typeof(DecompiledSourcesExternalSourcesProvider)
                        || provider.GetType() == typeof(MetadataViewerExternalSourcesProvider)).Select(provider => new {
                            id = provider.Id, runtimeType = provider.GetType().FullName,
                            applicable = provider.IsApplicableForNavigation(info, ignoreOptions: false)
                        }).ToArray();
                    samples.Add(new {
                        seq = ++sequence, elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds, epoch = index.Epoch,
                        rawMode = bound.GetValue((ExternalSourcesSettings settings) => settings.NavigationMode).ToString(),
                        rawBodies = bound.GetValue((DecompilerSettings settings) => settings.DecompileMethodBodies),
                        consent = bound.GetValue((DecompilerSettings settings) => settings.DecompilerLegalNoticeAccepted),
                        effectiveMode = mode.GetNavigationMode().ToString(), effectiveBodies = options.GetDecompileMethodBodiesValue(bound),
                        active = Shell.Instance.GetComponent<ExternalSourcesActivation>().Active.Value,
                        initialUpdate = caches.IsInitialUpdateFinished.Value, primaryIdle = caches.IsPrimaryIdle.Value,
                        committed = psi.Files.AllDocumentsAreCommitted, forceCookie = ExternalSourcesForceSourceCookie.Check(),
                        usePdb = bound.GetValue((PdbNavigationSettings settings) => settings.UseSymbolFiles),
                        downloadPdb = bound.GetValue((PdbNavigationSettings settings) => settings.Download),
                        sourceLink = bound.GetValue((PdbNavigationSettings settings) => settings.SourceLinkV3), providers
                    });
                    if (samples.Count > 128) samples.RemoveAt(0);
                    File.WriteAllText(Path.Combine(directory, "external-policy-observation.json"), JsonSerializer.Serialize(new {
                        seam = "read-only target SDK policy; not navigation or external failure acceptance", backendPid = Environment.ProcessId, samples
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(directory, "external-policy-error.json"), JsonSerializer.Serialize(new { error = error.GetType().Name }));
                }
                finally { Interlocked.Exchange(ref queued, 0); }
            });
        }, null, 1000, 1000);
        lifetime.OnTermination(() => timer?.Dispose());
    }
}
