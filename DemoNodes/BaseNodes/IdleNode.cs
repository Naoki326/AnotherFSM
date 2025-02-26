namespace StateMachine
{
    [FSMNode("Idle", "空转节点", [1], ["NextEvent"], Id = 2)]
    public class IdleNode : AsyncEnumFSMNode
    {
        public IdleNode()
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
