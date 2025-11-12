namespace StateMachine
{
    [FSMNode("Start", "启动节点", [1], ["NextEvent"], Id = 0)]
    public partial class StartNode : AsyncEnumFSMNode
    {

        protected override async IAsyncEnumerable<object> ExecuteEnumerable()
        {
            yield return Yield.None;
            try
            {
                await Task.Delay(1000, Context.Token);
            }
            catch (OperationCanceledException)
            { }
            yield return Yield.Priority(1);
            yield return Yield.None;
            PublishEvent(FSMEnum.Next);
        }
    }
}
