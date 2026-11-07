using FixtureSafety;
using StateMachine;
using System.Runtime.Versioning;
[assembly: SupportedOSPlatform("browser")]
namespace Issue34.StaticBrowser;
[FSMNode("BrowserCanvasKey")]
public sealed class CanvasWorker : ControlledNode { }
