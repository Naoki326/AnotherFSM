using System;
// Intentionally the same fully qualified names but a DIFFERENT assembly identity.
namespace StateMachine;
public interface IFSMNode { void Execute(); }
[AttributeUsage(AttributeTargets.Class)]
public sealed class FSMNodeAttribute : Attribute
{
    public FSMNodeAttribute(string key) => throw new InvalidOperationException("ISSUE34_BUSINESS_EXECUTION: impostor attribute ctor");
    public string Key => throw new InvalidOperationException("ISSUE34_BUSINESS_EXECUTION: impostor Key.get");
}
