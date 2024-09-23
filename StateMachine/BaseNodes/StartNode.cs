namespace StateMachine
{
    [FSMNode("Start", "启动节点", [1], ["NextEvent"], Id = 0)]
    public class StartNode : AsyncEnumFSMNode
    {

        protected override async IAsyncEnumerable<object> ExecuteEnumerable()
        {
            yield return null;
            try
            {
                await Task.Delay(1000, Context.Token);
            }
            catch (OperationCanceledException ex)
            { }
            yield return null;
            PublishEvent(FSMEnum.Next);
        }
    }
}
