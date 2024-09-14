﻿using StateMachine.Interface;

namespace StateMachine.Implement
{
    [FSMNode("Accumulate", "累积计数节点", [1, 4], ["NextEvent", "BreakEvent"], Id = 5)]
    public class AccumulateNode : AsyncEnumFSMNode
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
            yield return null;
            try
            {
                await Task.Delay(500, Context.Token);
            }
            catch (OperationCanceledException ex)
            { }
            yield return null;
            if (i < Count)
            {
                i++;
                PublishEvent(FSMEnum.Next);
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