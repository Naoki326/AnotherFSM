namespace StateMachine
{
    public class FSMException : Exception
    {
        public FSMException(string message) : base(message)
        { }

        public FSMException(string message, Exception innerException) : base(message, innerException)
        { }
    }
}
