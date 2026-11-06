using System.Text.Json;
using AnotherFSM.Rider;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;

namespace AnotherFSM.Acceptance;

/// <summary>Public query-seam regression: a list of uses must not become a unique declaration merely because it has one row.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class NavigationStatusProbe
{
    public NavigationStatusProbe(Lifetime lifetime, BridgeQueries queries)
    {
        var directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        _ = Run();
        async Task Run()
        {
            var checks = new List<object>();
            var passed = true;
            try
            {
                foreach (var test in new[] {
                    (Name: "one implicit Event use remains a location list", Text: "def Only(Key) {1->Pulse;}", Token: "Pulse", Operation: "declaration"),
                    (Name: "overwritten Output publisher is not a unique declaration", Text: "module M { output Done; def A(Key) {1->Done;1->Other;} }", Token: "Done", Operation: "internal")
                })
                {
                    var id = Guid.NewGuid().ToString();
                    var envelope = JsonSerializer.Serialize(new { action = "resolve", path = "query-probe.fsm", text = test.Text,
                        documentVersion = 1, offset = test.Text.IndexOf(test.Token, StringComparison.Ordinal), requestId = id, operation = test.Operation });
                    var raw = await queries.Query(lifetime, envelope).ConfigureAwait(false);
                    using var response = JsonDocument.Parse(raw);
                    var reply = response.RootElement;
                    var status = reply.GetProperty("status").GetString();
                    var count = reply.GetProperty("locations").GetArrayLength();
                    var valid = status == "locations" && count == 1;
                    passed &= valid;
                    checks.Add(new { name = test.Name, passed = valid,
                        requestId = id, status, count, reason = reply.GetProperty("reason").GetString() });
                }
                if (!lifetime.IsAlive) return;
                File.WriteAllText(Path.Combine(directory, "sdk-navigation-status.json"), JsonSerializer.Serialize(new {
                    status = passed ? "passed" : "failed",
                    seam = "BridgeQueries.Query in the target SDK; no claim of UI action acceptance", checks
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error)
            {
                if (lifetime.IsAlive) File.WriteAllText(Path.Combine(directory, "sdk-navigation-status.json"),
                    JsonSerializer.Serialize(new { status = "failed", error = error.ToString(), checks }));
            }
        }
    }
}
