using FixtureSafety;
namespace Issue34.Boundary;
[StateMachine.FSMNode("BoundaryProjectKey")]
public sealed class ProjectWorker : ControlledNode { }
#if NET8_0
[StateMachine.FSMNode("BoundaryFrameworkKey")]
public sealed class LegacyWorker : ControlledNode { }
#else
[StateMachine.FSMNode("BoundaryFrameworkKey")]
public sealed class ModernWorker : ControlledNode { }
#endif
