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
            yield return null;
            try
            {
                await Task.Delay(500, Context.Token);
            }
            catch (OperationCanceledException ex)
            { }
            yield return null;
            PublishEvent(FSMEnum.Next);
            yield break;
        }
    }
}
