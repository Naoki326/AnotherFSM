using DemoNodes;
using StateMachine;

namespace FSMDemo.Runtime;

/// <summary>C# host pairing, not a dependency container or a transport payload.</summary>
public abstract class HostContext
{
    public abstract void Validate(FSMEngine engine);
    public abstract FSMContextPreparation Prepare(FSMEngine engine);
    public virtual int? BusinessResult => null;
    public virtual int? BusinessRun => null;

    public static HostContext Create<TInput>(Func<TInput> input, Action<FSMContextBindings<TInput>>? configure = null)
        where TInput : class => new Typed<TInput>(input, configure);

    private sealed class Typed<TInput>(Func<TInput> input, Action<FSMContextBindings<TInput>>? configure) : HostContext where TInput : class
    {
        public override void Validate(FSMEngine engine) => engine.BindContext(configure);
        public override FSMContextPreparation Prepare(FSMEngine engine) => engine.BindContext(configure).Prepare(input());
    }
}

/// <summary>Both production hosts explicitly install this aggregate for their editable demo workspace.</summary>
public sealed class DemoBusinessHost : HostContext
{
    private DemoBusinessInput? current;
    private int run;
    public static DemoBusinessHost Create() => new();
    public override void Validate(FSMEngine engine) => engine.BindContext<DemoBusinessInput>();
    public override FSMContextPreparation Prepare(FSMEngine engine)
    {
        var input = new DemoBusinessInput();
        var prepared = engine.BindContext<DemoBusinessInput>().Prepare(input);
        current = input;
        ++run;
        return prepared;
    }
    public override int? BusinessResult => current?.Job.Value;
    public override int? BusinessRun => current == null ? null : run;
}
