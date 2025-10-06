using System.Security.Cryptography.X509Certificates;

namespace StateMachine
{
    public class ExcecuterContext : IExcecuterContext
    {
        public int Index { get; set; } = 0;

        public bool Condition { get; set; } = false;

        public string LastNodeName { get; set; } = "";

        public string CurrentNodeName { get; set; } = "";

        string IExcecuterContext.LastNodeName => LastNodeName;
        string IExcecuterContext.CurrentNodeName => CurrentNodeName;

        private HashSet<long> pauseAnchors = [];
        public HashSet<long> PauseAnchors
        {
            get => pauseAnchors;
            internal set { pauseAnchors = value; PauseAnchorsChanged?.Invoke(pauseAnchors); }
        }

        internal event Action<HashSet<long>> PauseAnchorsChanged;

        internal void RaisePauseAnchorsChnaged(HashSet<long> v)
        {
            PauseAnchorsChanged?.Invoke(v);
        }
    }
}
