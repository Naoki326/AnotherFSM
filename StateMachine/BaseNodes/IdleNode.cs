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
