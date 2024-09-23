using StateMachine;
using StateMachine.Interface;

namespace DemoShared.StateMachine
{
    [FSMNode("Sleep", "休眠节点", [1], ["NextEvent"], Id = 10)]
    public class SleepNode : AsyncEnumFSMNode
    {
        public SleepNode()
        {
        }

        [FSMProperty("Duration of time", true, 3)]
        public int Duration { get; set; } = 1000;

        protected override async IAsyncEnumerable<object> ExecuteEnumerable()
        {
            yield return null;
            try
            {
                await Task.Delay(Duration, Context.Token);
            }
            catch (OperationCanceledException)
            { }
            yield return null;
            PublishEvent(FSMEnum.Next);
            yield break;
        }
    }
}
