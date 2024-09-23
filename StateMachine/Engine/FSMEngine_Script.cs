using Antlr4.Runtime;

namespace StateMachine
{
    //通过脚本创建流程结构
    public partial class FSMEngine
    {
        public void CreateStateMachine(string input)
        {
            UnhandleGroupNode();

            ClearEvents();
            ClearNodes();
            var stream = new AntlrInputStream(input);
            var lexer = new StateMachineScriptLexer(stream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new StateMachineScriptParser(tokens);
            var tree = parser.machine();

            var state = new BuildStateVisitor(EventDict, NodeDict);
            state.Visit(tree);
            var transition = new BuildTransitionVisitor(EventDict, NodeDict);
            transition.Visit(tree);

            HandleGroupNode();
        }

        public void ReinitGroupNode()
        {
            UnhandleGroupNode();
            HandleGroupNode();
        }

        private void UnhandleGroupNode()
        {
            foreach (var node in NodeDict.Where(n => n.Value is BaseGroupNode))
            {
                var gn = (BaseGroupNode)node.Value;
                gn.NodeStateChanged -= Gn_NodeStateChanged;
                gn.NodeExitChanged -= Gn_NodeExitChanged;
            }
        }

        private void HandleGroupNode()
        {
            foreach (var node in NodeDict.Where(n => n.Value is BaseGroupNode))
            {
                var gn = (BaseGroupNode)node.Value;
                gn.SetEngine(this);
                gn.NodeStateChanged += Gn_NodeStateChanged;
                gn.NodeExitChanged += Gn_NodeExitChanged;
            }
        }

        private void Gn_NodeExitChanged(object sender, string e)
        {
            GroupNodeExitChanged(sender, e);
        }

        private void Gn_NodeStateChanged(object sender, string e)
        {
            GroupNodeStateChanged(sender, e);
        }

        public event EventHandler<string> GroupNodeStateChanged;
        public event EventHandler<string> GroupNodeExitChanged;

        public bool TryCreateStateMachine(string input)
        {
            try
            {
                CreateStateMachine(input);

                return true;
            }
            catch (Exception)
            {
                //throw;
                return false;
            }
        }

        public void CreateStateMachineByFile(string path)
        {
            using (FileStream f = new FileStream(path, FileMode.Open))
            using (StreamReader reader = new StreamReader(f))
                CreateStateMachine(reader.ReadToEnd());
        }

        public bool TryCreateStateMachineByFile(string path)
        {
            try
            {
                CreateStateMachineByFile(path);
                return true;
            }
            catch (Exception)
            {
                //throw;
                return false;
            }
        }

        public void Transform(string input)
        {
            UnhandleGroupNode();

            var stream = new AntlrInputStream(input);
            var lexer = new StateMachineScriptLexer(stream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new StateMachineScriptParser(tokens);
            var tree = parser.machine();

            foreach (var state in NodeDict.Values)
            {
                state.ClearTransition();
            }

            var transition = new BuildTransitionVisitor(EventDict, NodeDict);
            transition.Visit(tree);

            HandleGroupNode();
        }

        //变形：删除原有连线，重新连线
        public bool TryTransform(string input)
        {
            try
            {
                Transform(input);
                return true;
            }
            catch (Exception)
            {
                //throw;
                return false;
            }
        }

        public void TransformByFile(string path)
        {
            using (FileStream f = new FileStream(path, FileMode.Open))
            using (StreamReader reader = new StreamReader(f))
                Transform(reader.ReadToEnd());
        }

        public bool TryTransformByFile(string path)
        {
            try
            {
                TransformByFile(path);
                return true;
            }
            catch (Exception)
            {
                //throw;
                return false;
            }
        }


        public override string ToString()
        {
            string script = "";
            foreach (var state in this)
            {
                script += state.ToString();
            }
            return script;
        }

    }
}
