namespace StateMachine
{
    /// <summary>Business dependency contract used by the common assembly validator.
    /// A null ContextType explicitly declares no business dependency.</summary>
    public interface IFSMContextNode
    {
        Type? ContextType { get; }
    }

    // Binding is framework-owned; public node contracts expose no execution-time rebinding entry.
    internal interface IFSMContextBindingTarget
    {
        void BindContext(object? value);
    }

    public interface IFSMContextNode<T> : IFSMContextNode where T : class
    {
        T Context { get; }
    }
}
