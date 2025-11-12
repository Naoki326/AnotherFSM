using StateMachine.Interface;

namespace StateMachine
{
    [FSMNode("Accumulate", "累积计数节点", [1, 3], ["NextEvent", "BreakEvent"], Id = 5)]
    public partial class AccumulateNode : AsyncEnumFSMNode
    {
        public AccumulateNode()
        {
        }

        [FSMProperty("Count", true, 3)]
        public int Count { get; set; }

        private int i = 0;
        public override void InitBeforeStart()
        {
            i = 0;
        }

        protected override async IAsyncEnumerable<object> ExecuteEnumerable()
        {
            yield return Yield.None;
            try
            {
                await Task.Delay(500, Context.Token);
            }
            catch (OperationCanceledException)
            { }
            yield return Yield.None;
            if (Count < 0)
            {
                yield return Yield.Next;
                //PublishEvent(FSMEnum.Next);
            }
            else if (i < Count)
            {
                i++;
                yield return Yield.Event(1);
                //PublishEvent(FSMEnum.Next);
            }
            else
            {
                i = 0;
                PublishEvent(FSMEnum.Break);
            }
            yield break;
        }
    }
}
