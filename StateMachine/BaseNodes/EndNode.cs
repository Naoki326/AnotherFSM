namespace StateMachine
{
    [FSMNode("End", "结束节点", [1], ["EndEvent"], Id = 1)]
    public class EndNode : AsyncEnumFSMNode
    {
        public EndNode()
        {
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
            PublishEvent(FSMEnum.Next);
            yield break;
        }
    }
}
