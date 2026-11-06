using System.Text.Json;
using AnotherFSM.Rider;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.Application.UI.PopupLayout;
using JetBrains.IDE;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Navigation.Descriptors;
using JetBrains.ReSharper.Feature.Services.Occurrences;

namespace AnotherFSM.Acceptance;

/// <summary>Separate SDK action probe; the observation sampler never executes navigation.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class StaleNavigationProbe
{
    public StaleNavigationProbe(Lifetime lifetime, ISolution solution, ScriptIndex index,
        SearchDescriptorRegistrations registrations, IMainWindowPopupWindowContext popup)
    {
        var directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        var probed = false;
        void Check() => solution.Locks.QueueReadLock(lifetime, "AnotherFSM.TestStaleNavigation", () => {
            if (probed) return;
            IOccurrence? occurrence = null;
            foreach (var descriptor in registrations.SearchDescriptors)
            {
                descriptor.RwLock.ReadLock.Acquire();
                try { occurrence = descriptor.Items.FirstOrDefault(item => item.GetType().FullName == "AnotherFSM.Rider.FsmOccurrence" && !item.IsValid); }
                finally { descriptor.RwLock.ReadLock.Release(); }
                if (occurrence != null) break;
            }
            if (occurrence == null) return;
            probed = true;
            object result;
            try
            {
                // This is the same public occurrence entry used by RiderUsageList.NavigateTo.
                var navigated = occurrence.Navigate(solution, popup.Source, false, TabOptions.Default);
                result = new { passed = !navigated, valid = occurrence.IsValid, navigated, epoch = index.Epoch };
            }
            catch (Exception error) { result = new { passed = false, error = error.ToString() }; }
            File.WriteAllText(Path.Combine(directory, "stale-navigation-probe.json"), JsonSerializer.Serialize(result));
        });
        index.Invalidated += Check;
        lifetime.OnTermination(() => index.Invalidated -= Check);
    }
}
