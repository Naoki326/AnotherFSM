using JetBrains.Application.Progress;
using JetBrains.Application.Settings;
using JetBrains.Application.Threading;
using JetBrains.Application.UI.PopupLayout;
using JetBrains.Application.UI.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.ExternalSources.MetadataViewer;
using JetBrains.ReSharper.ExternalSources.ReSharperIntegration;
using JetBrains.ReSharper.Feature.Services.ExternalSource;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Core;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Resources.Shell;

namespace AnotherFSM.Rider;

/// <summary>
/// Presents an already bound compiled type, not source-match evidence. Call in the host's
/// committed main-thread read context (CommitWithRetryMainRead). This synchronous method
/// uses the host's primary-thread task executor; editor opening is only dispatched, not awaited.
/// It must not be called from a background read or while holding a write lock.
/// </summary>
public static class ExternalTypeNavigation
{
    public sealed record TypeMetadata(string ClrName, string Module, string TargetFramework,
        string AssemblyFullName, string AssemblyId, string AssemblyLocation, uint MetadataToken,
        uint ModuleToken);

    // Plain data, suitable for a bridge to serialize. Neither dispatch nor PDB/source paths
    // establish a source match. All outcomes here deliberately leave it unverified.
    public sealed record Result(string Status, string Reason, TypeMetadata? Metadata = null)
    {
        public string SourceMatch => "unverified";
    }

    public static Result Navigate(ITypeElement type, Func<bool> bindingCurrent)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(bindingCurrent);
        if (!type.IsValid() || !type.Module.IsValid())
            return new("unavailable", "The bound type is no longer valid; resolve it again");
        if (type is not ICompiledElement compiled || type.Module is not IAssemblyPsiModule module)
            return new("unsupported", "This entry point only presents already loaded compiled types");

        var metadata = new TypeMetadata(type.GetClrName().FullName, module.GetPersistentID(),
            module.TargetFrameworkId.UniqueString, module.Assembly.AssemblyName.FullName,
            module.Assembly.Id.ToString(), module.Assembly.Location?.AssemblyPhysicalPath.ToString() ?? "unavailable",
            compiled.Token, compiled.ModuleToken);
        var identity = $"{metadata.ClrName}; assembly={metadata.AssemblyFullName}; "
            + $"assemblyId={metadata.AssemblyId}; location={metadata.AssemblyLocation}; "
            + $"module={metadata.Module}; framework={metadata.TargetFramework}; "
            + $"token=0x{metadata.MetadataToken:X8}; moduleToken=0x{metadata.ModuleToken:X8}";
        Result MetadataOnly(string reason) => new("metadata", identity + "; " + reason
            + "; source match unverified", metadata);

        try
        {
            if (!bindingCurrent()) return MetadataOnly("Binding context expired; no navigation dispatched");
            var psi = type.GetPsiServices();
            var solution = psi.Solution;
            if (!solution.Locks.IsOnMainThread() || !solution.Locks.IsReadAccessAllowed() || solution.Locks.IsWriteLockHeld())
                return MetadataOnly("Presentation requires the caller's committed main-thread read context (not a write lock)");
            if (!psi.Files.AllDocumentsAreCommitted)
                return MetadataOnly("Host PSI documents are not committed; retry after commit");
            var caches = solution.GetComponent<IPsiCachesState>();
            if (!caches.IsInitialUpdateFinished.Value || !caches.IsPrimaryIdle.Value)
                return MetadataOnly("Host PSI caches are not initialized/primary-idle; retry when ready");
            if (!Shell.Instance.GetComponent<ExternalSourcesActivation>().Active.Value)
                return MetadataOnly("Host external-sources subsystem is inactive; no activation or settings change was requested");
            // Require the module which the positioning helper would use to already be the
            // bound module. Do not repair/retarget a missing or different primary module.
            if (!ReferenceEquals(psi.Modules.GetPrimaryPsiModule(module.ContainingProjectModule,
                    module.Assembly.TargetFrameworkId), module))
                return MetadataOnly("The bound assembly's primary PSI module is unavailable or different; no retarget attempted");
            var service = solution.GetComponent<IExternalSourcesService>();
            var executor = Shell.Instance.GetComponent<UITaskExecutor>().SingleThreaded;
            // ToNavigationInfo does not retarget. Never call target.Navigate or the default
            // ExternalSourcesPointsProvider.CreateNavigationPoints/GetSourcePoint entry points.
            var info = compiled.ToNavigationInfo();
            var reasons = new List<string>();
            foreach (var id in new[] { "decompiler", "metadata" })
            {
                if (!bindingCurrent()) return MetadataOnly("Binding context expired; no navigation dispatched");
                // An ID alone is not a safety boundary: require the exact SDK implementation,
                // excluding replacement/subclass providers with unreviewed side effects.
                var expected = id == "decompiler" ? typeof(DecompiledSourcesExternalSourcesProvider)
                    : typeof(MetadataViewerExternalSourcesProvider);
                IExternalSourcesProvider[] providers = service.OrderedProviders
                    .Where(p => p.GetType() == expected && p.Id == id).ToArray();
                if (providers.Length != 1)
                {
                    reasons.Add(id + " provider unavailable or ambiguous");
                    continue;
                }
                try
                {
                    if (!providers[0].IsApplicableForNavigation(info, ignoreOptions: false))
                    {
                        reasons.Add(id + " is not applicable under current host options");
                        continue;
                    }
                    if (id == "decompiler" && !Shell.Instance.GetComponent<ISettingsStore>()
                        .BindToContextTransient(ContextRange.ApplicationWide)
                        .GetValue((DecompilerSettings s) => s.DecompilerLegalNoticeAccepted))
                    {
                        // CheckAndAskUser would persist consent. We do not ask or change settings.
                        reasons.Add("decompiler legal notice has not already been accepted; no settings were changed");
                        continue;
                    }
                    var mappings = service.NavigateToSources(info, providers, executor, ignoreFilteringByOptions: false);
                    if (mappings == null)
                        return MetadataOnly(string.Join("; ", reasons.Append(id
                            + " provider handled or cancelled the request; presentation not confirmed")));
                    if (mappings.Count == 0)
                    {
                        reasons.Add(id + " produced no presentation (host supplied no further cause)");
                        continue;
                    }
                    if (!bindingCurrent() || !type.IsValid() || !module.IsValid())
                        return MetadataOnly("Binding/type/module expired while generating the presentation; no navigation dispatched");
                    if (mappings.Any(m => !ReferenceEquals(m.Provider, providers[0]) || m.IsUserFile
                        || !ReferenceEquals(m.Assembly, module.ContainingProjectModule)))
                        return MetadataOnly("Provider returned a mapping outside the bound assembly/presentation");

                    // Use only the public parsing/positioning helper, not its retargeting entry
                    // points. OriginalNavigationInfo is null, so there is no alternate target.
                    FileSystemPathNavigationPoint[]? points = null;
                    executor.ExecuteTask("AnotherFSM: " + id + " (not verified source)", TaskCancelable.Yes, indicator => {
                        // Follow the SDK positioning path's explicit read/commit requirements.
                        using var read = ReadLockCookie.Create("rider/backend/ExternalTypeNavigation.cs", nameof(Navigate));
                        psi.Files.AssertAllDocumentAreCommitted();
                        points = solution.GetComponent<ExternalSourcesPointsProvider>()
                            .FindSpecificElementInFile(info, mappings, indicator, out _);
                    });
                    if (points == null || points.Length == 0)
                    {
                        reasons.Add(id + " produced a presentation but no navigation position");
                        continue;
                    }
                    if (!bindingCurrent() || !type.IsValid() || !module.IsValid() || !psi.Files.AllDocumentsAreCommitted)
                        return MetadataOnly("Binding/type/module/documents expired during positioning; no navigation dispatched");
                    var options = NavigationOptions.FromWindowContext(
                        Shell.Instance.GetComponent<IMainWindowPopupWindowContext>().Source,
                        "AnotherFSM: " + (id == "decompiler" ? "decompiled code" : "metadata")
                            + " (not verified source)", true);
                    if (NavigationManager.GetInstance(solution).Navigate(points, options))
                        return new(id == "decompiler" ? "decompiledNavigationDispatched" : "metadataNavigationDispatched",
                            identity + "; " + (reasons.Count == 0 ? "" : string.Join("; ", reasons) + "; ")
                            + (id == "decompiler" ? "Decompiled code" : "Metadata view")
                            + " navigation dispatched to Rider; source match unverified; editor opening not confirmed", metadata);
                    reasons.Add(id + " navigation was not dispatched");
                }
                catch (OperationCanceledException)
                {
                    return MetadataOnly(string.Join("; ", reasons.Append(id + " presentation cancelled")));
                }
                catch (Exception ex)
                {
                    // Do not infer network/authentication/offline causes, or expose exception
                    // messages (which can contain private locations or credentials).
                    reasons.Add(id + " presentation failed (" + ex.GetType().Name + "); underlying external cause unknown");
                }
            }
            return MetadataOnly(string.Join("; ", reasons));
        }
        catch (OperationCanceledException)
        {
            return MetadataOnly("Presentation cancelled");
        }
        catch (Exception ex)
        {
            return MetadataOnly("Host presentation unavailable (" + ex.GetType().Name + "); underlying external cause unknown");
        }
    }
}
