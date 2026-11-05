namespace StateMachine
{
    //通过脚本创建流程结构
    public partial class FSMEngine
    {
        internal string? CurrentScriptDirectory { get; private set; }

        public void CreateStateMachine(string input, string? directoryPath = null)
        {
            CurrentScriptDirectory = directoryPath;

            ClearEvents();
            ClearNodes();
            moduleInstances.Clear();
            var tree = FsmScriptParser.Parse(input);

            var state = new BuildStateVisitor(eventDict, nodeDict, nodeFactory, this);
            state.Visit(tree);
            var transition = new BuildTransitionVisitor(eventDict, nodeDict, moduleInstances);
            transition.Visit(tree);

            InjectNodeEngines();
        }

        // Historical name retained: this serves every IFSMEngineInjectable node,
        // not runtime composition.
        public void ReinitGroupNode() => InjectNodeEngines();

        internal void InjectNodeEngines()
        {
            foreach (var node in nodeDict)
            {
                if (node.Value is IFSMEngineInjectable injectable)
                    injectable.SetEngine(this);
            }
        }

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
            var dir = Path.GetDirectoryName(path);
            using (FileStream f = new FileStream(path, FileMode.Open))
            using (StreamReader reader = new StreamReader(f))
                CreateStateMachine(reader.ReadToEnd(), dir);
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
            var tree = FsmScriptParser.Parse(input);

            foreach (var state in nodeDict.Values)
            {
                state.ClearTransition();
            }

            var transition = new BuildTransitionVisitor(eventDict, nodeDict, moduleInstances);
            transition.Visit(tree);

            InjectNodeEngines();
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
