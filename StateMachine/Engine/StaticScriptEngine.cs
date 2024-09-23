using Antlr4.Runtime;
using System.Collections;

namespace StateMachine
{
    public class StaticScriptEngine : IEnumerable<IFSMNode>, IEnumerable<string>
    {
        protected static Dictionary<string, FSMEvent> EventDict = new Dictionary<string, FSMEvent>();
        protected Dictionary<string, IFSMNode> NodeDict = new Dictionary<string, IFSMNode>();
        public bool CreateStateMachine(string input)
        {
            try
            {
                var stream = new AntlrInputStream(input);
                var lexer = new StateMachineScriptLexer(stream);
                var tokens = new CommonTokenStream(lexer);
                var parser = new StateMachineScriptParser(tokens);
                var tree = parser.machine();

                var state = new BuildStateVisitor(EventDict, NodeDict);
                state.Visit(tree);
                var transition = new BuildTransitionVisitor(EventDict, NodeDict);
                transition.Visit(tree);

                return true;
            }
            catch (Exception)
            {
                throw;
                //return false;
            }
        }

        public bool CreateStateMachineByFile(string path)
        {
            try
            {
                using (var f = new FileStream(path, FileMode.Open))
                {
                    using (var reader = new StreamReader(f))
                    {
                        return CreateStateMachine(reader.ReadToEnd());
                    }
                }
            }
            catch (Exception)
            {
                throw;
                //return false;
            }
        }

        public IEnumerable<string> GetStateNames()
        {
            return NodeDict.Keys;
        }

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
        {
            return NodeDict.Keys.GetEnumerator();
        }

        public IEnumerator<IFSMNode> GetEnumerator()
        {
            return NodeDict.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return NodeDict.Values.GetEnumerator();
        }

        public IFSMNode this[string name]
        {
            get { return GetState(name); }
        }

        public IFSMNode GetState(string name)
        {
            if (!NodeDict.TryGetValue(name, out IFSMNode value))
            { return null; }
            return value;
        }

        public static IEnumerable<string> GetEventNames()
        {
            return EventDict.Keys;
        }

        public static FSMEvent StaticGetEvent(string name)
        {
            if (!EventDict.TryGetValue(name, out FSMEvent value))
            { return null; }
            return value;
        }

        public static void PublishEvent(string name)
        {
            if (!EventDict.TryGetValue(name, out FSMEvent value))
            { return; }
            FSMEventAggregator.EventAggregator.Publish(value);
        }

        public static void PublishEvent<T>(string name, T eventContext)
        {
            if (!EventDict.TryGetValue(name, out FSMEvent value))
            { return; }
            var eventValue = value;
            eventValue.EventContext = eventContext;
            FSMEventAggregator.EventAggregator.Publish(eventValue);
        }

        public static void PublishEvent(string name, object eventContext)
        {
            PublishEvent<object>(name, eventContext);
        }

    }
}
