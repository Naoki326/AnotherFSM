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
                await Task.Delay(5000, Context.Token);
            }
            catch (OperationCanceledException)
            { }
            yield return Yield.RestoreIfPause(async () =>
            {
                await Task.Delay(10000, Context.Token);
            });
            yield return Yield.None;
            PublishEvent(FSMEnum.Next);
            yield break;
        }
    }
}
