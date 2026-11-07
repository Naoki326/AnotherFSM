using FixtureSafety;
using StateMachine;

namespace Issue34.StaticWasm;

[FSMNode("WasmSurfaceKey")]
public sealed class SurfaceWorker : ControlledNode
{
    public SurfaceWorker() => Execution.Forbidden<object>("SurfaceWorker.ctor");
    public override void Execute() => Execution.Forbidden<object>("SurfaceWorker.Execute");
}
