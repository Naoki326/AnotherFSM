using System.Text.Json;
using AnotherFSM.Rider.Protocol;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.Rd.Tasks;
using JetBrains.ReSharper.Feature.Services.Protocol;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace AnotherFSM.Rider;

[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class ProtocolHost
{
    private static readonly ILogger Log = Logger.GetLogger("AnotherFSM");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public ProtocolHost(Lifetime lifetime, ISolution solution, ScriptIndex index, BridgeQueries queries, ResultLifecycle results)
    {
        var root = solution.GetProtocolSolution();
        var scheduler = root.TryGetProto()?.Scheduler ?? throw new InvalidOperationException("Rider Solution protocol is not bound");
        scheduler.Queue(() => {
            if (!lifetime.IsAlive) return;
            var model = root.GetAnotherFsmModel();
            model.Query.SetAsync(async (requestLifetime, json) => {
                Log.Info("AFSM32 RD query received");
                var result = await queries.Query(requestLifetime, json).ConfigureAwait(false);
                Log.Info("AFSM32 RD query completed");
                return result;
            });
            void Invalidate() => scheduler.Queue(() => {
                if (lifetime.IsAlive) model.Invalidated.Fire(JsonSerializer.Serialize(new { session = index.Session, contextVersion = index.Epoch }));
            });
            void Expire(IReadOnlyList<ReferenceQueryStatus> status) => scheduler.Queue(() => {
                if (lifetime.IsAlive) model.Invalidated.Fire(JsonSerializer.Serialize(new {
                    session = index.Session, contextVersion = index.Epoch, expiredReferences = status }, Json));
            });
            index.Invalidated += Invalidate;
            results.ReferencesExpired += Expire;
            lifetime.OnTermination(() => { index.Invalidated -= Invalidate; results.ReferencesExpired -= Expire; });
            Log.Info("AFSM32 RD handler installed session=" + index.Session);
            Invalidate();
        });
    }
}
