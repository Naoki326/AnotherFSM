using System.Reactive.Disposables;

namespace StateMachine
{
    // AsyncLocal captures the originating round even for a late asynchronous notification.
    internal static class FSMNodeEventScope
    {
        private sealed class Route
        {
            internal FSMEngine Engine = null!;
            internal object Invocation = null!;
            internal Action<FSMEvent> Publish = null!;
            internal Action Pause = null!;
        }
        private static readonly AsyncLocal<Route?> current = new();
        internal static IDisposable Enter(FSMEngine engine, object invocation, Action<FSMEvent> publish, Action pause)
        {
            var previous = current.Value;
            current.Value = new Route { Engine = engine, Invocation = invocation, Publish = publish, Pause = pause };
            return Disposable.Create(() => current.Value = previous);
        }
        internal static object? Invocation => current.Value?.Invocation;
        internal static void Pause() => current.Value?.Pause();
        internal static void Publish(FSMEvent value) => current.Value?.Publish(value);
        internal static void Publish(FSMEngine engine, FSMEvent value)
        {
            var route = current.Value;
            if (route == null || !ReferenceEquals(route.Engine, engine))
                throw new FSMException("External events require a concrete execution handle, not an engine's current execution.");
            route.Publish(value);
        }
    }
}
