using System;
using System.Collections.Generic;
using System.Text;

namespace StateMachine.ScriptAnalyzer
{
    internal class VisualNode : IVisualNode
    {

        //脚本中的命名空间
        public string NamePrefix { get; set; }

        //当前节点对象在脚本内的名称
        public string Name { get; set; }

        public double PosX { get; set; }

        public double PosY { get; set; }

        public string FlowID { get; set; }

        public string ClassType { get; set; }

        public string Color { get; set; }

        public string Discription { get; set; }

        public List<NodeEventDescription> EventDescriptions { get; set; } = [];

        public void UpdateEventDescriptions()
        {
        }
    }
}
