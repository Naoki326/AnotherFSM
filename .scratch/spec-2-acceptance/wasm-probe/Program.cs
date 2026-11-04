using FSMDemo.Wasm.Services;

using var runtime = new DemoRuntime();
if (runtime.Error != null) throw new Exception("Default Demo: " + runtime.Error);
Console.WriteLine("Default module projection: " + string.Join("; ", runtime.Nodes.Where(n => n.IsModuleInstance).Select(n => n.Name + "=[" + string.Join(",", n.Events) + "]")));
runtime.ImportScript("""
    module M { input Entry, Retry; output Done;
      def Entry(Idle) { 1->Done; }
      def Retry(Idle) { 1->Done; }
    }
    use M as A;
    def Start(Start) { 1->Launch; }
    def A.Observer(Idle) { 1->Observe; }
    def End(End) {}
    Launch -> Start to A.Retry;
    Observe -> A.Observer to End;
    A.Done -> End;
    """);
if (runtime.Error != null) throw new Exception("Probe import: " + runtime.Error);
var root = runtime.Nodes.Select(n => n.Name).ToArray();
Console.WriteLine("Root nodes: " + string.Join(", ", root));
Console.WriteLine("Root edges: " + string.Join("; ", runtime.Connections.Select(c => $"{c.Source} --{c.EventName}--> {c.Target}")));
runtime.EnterModule("A");
Console.WriteLine("Module A nodes: " + string.Join(", ", runtime.Nodes.Select(n => n.Name)));
if (!root.Contains("A.Observer"))
{
    Console.Error.WriteLine("FAIL: ordinary dotted node A.Observer is hidden from Root and collapsed into Module A although not an expanded member.");
    return 1;
}
return 0;
