// THROWAWAY guided console walkthrough, not a test suite or production API.
using Throwaway;

Console.WriteLine("THROWAWAY C# binding probe; names CheckA/CheckB are SIMULATED Module flattening.");
Console.WriteLine("\n1. Unique direct property; shared interfaces; freeze per execution");
var oldJob = new JobData("A");
oldJob.Tasks.Add("queued");
var replacement = new JobData("B");
var aggregate = new SingleJob(oldJob);
JobData expected = oldJob;
var automatic = new ProbeFlow<SingleJob>(new Bindings<SingleJob>(),
    new ProcessNode("CheckA.Process", context =>
    {
        Show(ReferenceEquals(context, expected), "Process holds expected JobData identity");
        aggregate.ReplaceInsideForProbe(replacement); // Counterexample only, not a production API recommendation.
        Console.WriteLine($"ACTION class-internal private-set replacement -> {aggregate.Job}");
    }),
    new QueryNode("CheckA.Query", context =>
    {
        Show(ReferenceEquals(context, expected), "Query holds same JobData despite aggregate replacement");
        Show(context.Tasks.Contains("processed"), "Tasks mutation is visible through IQueryContext");
    }));
automatic.Run(aggregate);
Show(ReferenceEquals(aggregate.Job, replacement) && !replacement.Tasks.Contains("processed"), "replacement B was not bound mid-execution");
Console.WriteLine("\n2. Next execution re-reads the aggregate: B, then a new aggregate with C");
expected = replacement;
automatic.Run(aggregate);
expected = new JobData("C");
aggregate = new SingleJob(expected);
automatic.Run(aggregate);

Console.WriteLine("\n3. Multiple candidates reject; DefaultSource resolves; CheckB.Query overrides");
Reject("ASSEMBLY", () => _ = new ProbeFlow<TwoJobs>(new Bindings<TwoJobs>(), new QueryNode("CheckA.Query", _ => { })));
var bindings = new Bindings<TwoJobs>();
TwoJobs.Configure(bindings); // Relations live with the script-specific aggregate type.
var pair = new TwoJobs { Job = new JobData("default"), OtherJob = new JobData("override") };
var configured = new ProbeFlow<TwoJobs>(bindings,
    new NoBusinessNode("Start"),
    new ProcessNode("CheckA.Process", c => Show(ReferenceEquals(c, pair.Job), "DefaultSource selected Job for Process")),
    new QueryNode("CheckA.Query", c => Show(ReferenceEquals(c, pair.Job) && c.Tasks.Contains("processed"), "default Query shares mutated Job")),
    new QueryNode("CheckB.Query", c => Show(ReferenceEquals(c, pair.OtherJob) && c.Tasks.Count == 0, "qualified override selects untouched OtherJob")));
configured.Run(pair);

Console.WriteLine("\n4. Typed Default<IQueryContext> positive example");
var typed = new Bindings<TwoJobs>();
typed.Default<IQueryContext>(x => x.Job);
new ProbeFlow<TwoJobs>(typed, new QueryNode("Query", c => Show(ReferenceEquals(c, pair.Job), "typed Default selects Job"))).Run(pair);

Console.WriteLine("\n5. Missing binding rejects assembly; no-business node needs no source");
Reject("ASSEMBLY", () => _ = new ProbeFlow<NoData>(new Bindings<NoData>(), new QueryNode("Missing.Query", _ => { })));
new ProbeFlow<NoData>(new Bindings<NoData>(), new NoBusinessNode("OnlyControl")).Run(new NoData());

Console.WriteLine("\n6. Null startup rejects before ANY node (including Start)");
Reject("START", () => configured.Run(null!));
Reject("START", () => configured.Run(new TwoJobs { Job = pair.Job, OtherJob = null! }));
Reject("START", () => configured.Run(new TwoJobs { Job = null!, OtherJob = new JobData("valid-alternative") }));
// No fallback to the other compatible property; invalid instances are initialized, not reassigned.
Console.WriteLine("\n7. Selected source with ordinary public set rejects assembly");
Reject("ASSEMBLY", () => _ = new ProbeFlow<PublicSetJob>(new Bindings<PublicSetJob>(),
    new NoBusinessNode("Start"), new QueryNode("PublicSet.Query", _ => { })));
Console.WriteLine($"\nDONE: guided observations held; total node calls={ProbeNode.Calls}. No DSL/runtime claims.");

#if BAD_BINDING
// Both must fail actual C# compilation: string cannot provide IQueryContext.
var bad = new Bindings<NoData>();
bad.Default<IQueryContext>(x => x.Text);
bad.Node<IQueryContext>("CheckB.Query", x => x.Text);
#endif
#if BAD_REPLACEMENT
// Both are external assignments AFTER initialization, and must fail compilation.
pair.Job = replacement;       // public init
aggregate.Job = replacement;  // private set
#endif

static void Show(bool condition, string observation)
{
    if (!condition) throw new Exception($"Observation failed: {observation}");
    Console.WriteLine($"OBSERVED {observation}");
}
static void Reject(string stage, Action action)
{
    int before = ProbeNode.Calls;
    try { action(); }
    catch (InvalidOperationException e) when (e.Message.StartsWith(stage + " ", StringComparison.Ordinal))
    {
        Console.WriteLine($"EXPECTED REJECTION: {e.Message}");
        Show(ProbeNode.Calls == before, $"{stage} rejection: nodes run=0");
        return;
    }
    throw new Exception($"Expected {stage} rejection did not occur");
}

public sealed class SingleJob(JobData job)
{
    public JobData Job { get; private set; } = job;
    // Probe-only counterexample: private set is not deep immutability. Do not copy this public escape hatch.
    public void ReplaceInsideForProbe(JobData next) => Job = next;
}
public sealed class PublicSetJob { public JobData Job { get; set; } = null!; }
public sealed class TwoJobs
{
    public JobData Job { get; init; } = null!;
    public JobData OtherJob { get; init; } = null!;
    public static void Configure(Bindings<TwoJobs> bindings)
    {
        bindings.DefaultSource(x => x.Job);
        bindings.Node<IQueryContext>("CheckB.Query", x => x.OtherJob);
    }
}
public sealed class NoData { public string Text { get; init; } = "not a business Context"; }
