using FixtureSafety;
using StateMachine;
namespace Issue34.MultiTarget;
#if NET8_0
[FSMNode("FrameworkBindingKey")]
public sealed class LegacyRouteWorker : ControlledNode { }
[FSMNode("Net8OnlyKey")]
public sealed class EightWorker : ControlledNode { }
#elif NET10_0
[FSMNode("FrameworkBindingKey")]
public sealed class ModernRouteWorker : ControlledNode { }
[FSMNode("Net10OnlyKey")]
public sealed class TenWorker : ControlledNode { }
#else
#error Unsupported controlled target framework.
#endif
