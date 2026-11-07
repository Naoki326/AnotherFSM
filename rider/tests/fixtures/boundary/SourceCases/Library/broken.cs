namespace Issue34.SourceCases;
[StateMachine.FSMNode("SourceBrokenILKey")]
public sealed class BrokenWorker : FixtureSafety.ControlledNode
{
    public int RenderProbe(int input)
    {
        FixtureSafety.Execution.Forbidden<object>("BrokenWorker.RenderProbe");
        return input + 34;
    }
}
