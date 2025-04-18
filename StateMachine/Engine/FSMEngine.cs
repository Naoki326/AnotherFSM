using System.Collections;
using System.Diagnostics;

namespace StateMachine
{
    public class ScriptNode
    {
        public ScriptNode(string name)
        {
            Name = name;
        }
        public string Name { get; }

        public static implicit operator string(ScriptNode d) => d.Name;
        public static explicit operator ScriptNode(string b) => new ScriptNode(b);
        public override string ToString()
        {
            return Name;
        }
    }

    public class ScriptEvent
    {
        public ScriptEvent(string name)
        {
            Name = name;
        }
        public string Name { get; }

        public static implicit operator string(ScriptEvent d) => d.Name;
        public static explicit operator ScriptEvent(string b) => new ScriptEvent(b);
        public override string ToString()
        {
            return Name;
        }
    }

    //通过脚本创建流程结构
    [DebuggerNonUserCode]
    public partial class FSMEngine : IEnumerable<IFSMNode>
    {

        protected Dictionary<string, FSMEvent> eventDict = new Dictionary<string, FSMEvent>();
        protected Dictionary<string, IFSMNode> nodeDict = new Dictionary<string, IFSMNode>();


        public IFSMNode this[string name]
        {
            get { return GetNode(name); }
        }

        public IFSMNode this[ScriptNode name]
        {
            get { return GetNode(name); }
        }

        public FSMEvent this[ScriptEvent name]
        {
            get { return GetEvent(name); }
        }

        public bool ContainsNode(string name)
        {
            return nodeDict.ContainsKey(name);
        }

        //public bool TryGetNode(string name, [MaybeNullWhen(false)] out IFSMNode node)
        public bool TryGetNode(string name, out IFSMNode node)
        {
            try
            {
                node = GetNode(name);
            }
            catch (Exception)
            {
                node = default!;
                return false;
            }
            return true;
        }

        public bool TryGetNode<T>(string name, out T node)
        {
            try
            {
                if (GetNode(name) is T result)
                {
                    node = result;
                }
                else
                {
                    node = default!;
                    return false;
                }
            }
            catch (Exception)
            {
                node = default!;
                return false;
            }
            return true;
        }

        public IFSMNode GetNode(string name)
        {
            if (!nodeDict.TryGetValue(name, out IFSMNode value))
            { throw new KeyNotFoundException($"Node {name} doesn't exist"); }
            return value;
        }

        public T GetNode<T>(string name) where T : IFSMNode
        {
            if (!nodeDict.TryGetValue(name, out IFSMNode value))
            { throw new KeyNotFoundException($"Node {name} doesn't exist"); }
            return (T)value;
        }

        public IEnumerable<string> GetNodeNames()
        {
            return nodeDict.Keys;
        }

        public IEnumerable<string> GetEventNames()
        {
            return eventDict.Keys;
        }

        public bool ContainsEvent(string eventName)
        {
            return eventDict.ContainsKey(eventName);
        }

        public bool TryGetEvent(string name, out FSMEvent e)
        {
            try
            {
                e = GetEvent(name);
                return true;
            }
            catch (Exception)
            {
                e = default!;
                return false;
            }
        }

        public FSMEvent GetEvent(string name)
        {
            if (!eventDict.TryGetValue(name, out FSMEvent value))
            { throw new KeyNotFoundException($"Event {name} doesn't exist"); }
            return value;
        }

        public void AddEvent(FSMEvent e)
        {
            if (eventDict.TryGetValue(e.EventName, out _))
            {
                throw new InvalidOperationException($"Event {e.EventName} already exist");
            }
            eventDict.Add(e.EventName, e);
        }

        public bool TryAddEvent(FSMEvent e)
        {
            if (!eventDict.TryGetValue(e.EventName, out _))
            {
                eventDict.Add(e.EventName, e);
                return true;
            }
            return false;
        }

        public void ClearEvents()
        {
            eventDict.Clear();
        }

        public void PublishEvent(string name)
        {
            if (!eventDict.TryGetValue(name, out FSMEvent? value))
            { return; }
            FSMEventAggregator.EventAggregator.Publish(value);
        }

        public void PublishEvent<T>(string name, T eventContext)
        {
            if (!eventDict.TryGetValue(name, out FSMEvent value))
            { return; }
            var eventValue = value;
            eventValue.EventContext = eventContext!;
            FSMEventAggregator.EventAggregator.Publish(eventValue);
        }

        public void PublishEvent(string name, object eventContext)
        {
            PublishEvent<object>(name, eventContext);
        }

        public IEnumerator<IFSMNode> GetEnumerator()
        {
            return nodeDict.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}