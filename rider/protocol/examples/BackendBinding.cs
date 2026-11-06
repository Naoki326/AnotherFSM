using System;
using System.Threading.Tasks;
using AnotherFSM.Rider.Protocol;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.Rd.Tasks;
using JetBrains.ReSharper.Feature.Services.Protocol;

namespace AnotherFSM.Rider.Protocol.Examples;

// Compile-checked seam, not a registered SolutionComponent.
// Acquire/install on the host protocol scheduler after the protocol Solution exists.
// The adapter owns PSI locks, cancellation, version checks and all JSON semantics.
public static class BackendBinding
{
    public static AnotherFsmModel Acquire(ISolution solution) =>
        solution.GetProtocolSolution().GetAnotherFsmModel();

    public static void InstallQueryHandler(
        AnotherFsmModel model, Func<Lifetime, string, Task<string>> handler) =>
        model.Query.SetAsync(handler);

    public static void Invalidate(AnotherFsmModel model, string envelope) =>
        model.Invalidated.Fire(envelope);
}
