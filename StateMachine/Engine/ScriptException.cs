namespace StateMachine
{
    public class ScriptException : Exception
    {
        public ScriptException(string message) : base(message)
        {
        }
        public ScriptException(string message, Exception e) : base(message, e)
        {
        }
    }
}
