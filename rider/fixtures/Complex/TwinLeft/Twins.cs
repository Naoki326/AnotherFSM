using FixtureSafety;
using Tag = StateMachine.FSMNodeAttribute;
namespace Issue34.SameNames;
#if TWIN_LEFT
[Tag("LeftAssemblyKey")]
#else
[Tag("RightAssemblyKey")]
#endif
public sealed class CollisionWorker : ControlledNode { }

// Same CLR name AND same factory key in different assemblies: never silently pick one.
[Tag("AmbiguousAssemblyKey")]
public sealed class DuplicateWorker : ControlledNode { }
