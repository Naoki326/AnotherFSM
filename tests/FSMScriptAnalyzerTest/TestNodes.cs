using StateMachine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace FSMScriptAnalyzerTest
{
    [FSMNode("TestStart", "测试启动节点", [1], ["NextEvent"], Id = 1000)]
    public class TestStartNode : AsyncEnumFSMNode
    {
        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            yield return Yield.Next;
            PublishEvent(FSMEnum.Next);
        }
    }

    [FSMNode("TestEnd", "测试结束节点", [1], ["NextEvent"], Id = 1001)]
    public class TestEndNode : AsyncEnumFSMNode
    {
        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            yield return Yield.Next;
            PublishEvent(FSMEnum.Next);
        }
    }

    [FSMNode("TestIdle", "测试空转节点", [1, 3], ["NextEvent", "ErrorEvent"], Id = 1002)]
    public class TestIdleNode : AsyncEnumFSMNode
    {
        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            try { await Task.Delay(100, Context.Token); }
            catch (OperationCanceledException) { }
            yield return Yield.Next;
            PublishEvent(FSMEnum.Next);
        }
    }

    [FSMNode("TestAcc", "测试计数节点", [1, 3], ["NextEvent", "BreakEvent"], Id = 1003)]
    public class TestAccNode : AsyncEnumFSMNode
    {
        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            yield return Yield.Next;
            PublishEvent(FSMEnum.Next);
        }
    }
}
