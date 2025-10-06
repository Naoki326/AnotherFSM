using System.Security.Cryptography.X509Certificates;

namespace StateMachine
{
    public class ExecuterContext : IExecuterContext
    {
        public int Index { get; set; } = 0;

        public bool Condition { get; set; } = false;

        public string LastNodeName { get; set; } = "";

        public string CurrentNodeName { get; set; } = "";

        private HashSet<long> pauseAnchors = [];
        public HashSet<long> PauseAnchors
        {
            get => pauseAnchors;
            internal set { pauseAnchors = value; }
        }
    }
}
